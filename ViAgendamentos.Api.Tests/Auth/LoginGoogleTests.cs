using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ViAgendamentos.Api.Tests.Data;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Tests.Auth;

public class LoginGoogleTests(PostgresFixtureTests banco)
{
    // A resposta de /api/auth/me como o front lê.
    private sealed record SessaoNaResposta(string Nome, string Email);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    // Pelo UserManager, como o app grava: com o e-mail normalizado que a busca por e-mail usa.
    private async Task UsuarioExistenteAsync(string email, string nome)
    {
        await using var escopo = banco.Api.Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var criado = await usuarios.CreateAsync(new Usuario { Nome = nome, UserName = email, Email = email, EmailConfirmed = true });
        Assert.True(criado.Succeeded, string.Join(" ", criado.Errors.Select(erro => erro.Description)));
    }

    private async Task<ContaGoogle> ContaQueJaEntrouAsync()
    {
        var conta = ContaGoogle.Nova();
        using var navegador = banco.NovoNavegador();
        await navegador.EntrarComGoogleAsync(banco.Google, conta);
        return conta;
    }

    // Em produção a API fica atrás de proxy e recebe http, mesmo com o navegador em https.
    private sealed class ProxyEmHttp : DelegatingHandler
    {
        public List<string> CookiesEmitidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
        {
            var original = pedido.RequestUri!;
            pedido.RequestUri = new UriBuilder(original) { Scheme = Uri.UriSchemeHttp, Port = -1 }.Uri;
            var resposta = await base.SendAsync(pedido, cancelamento);
            // O navegador guarda os cookies para o endereço que ele pediu, e não para o que a API recebeu.
            pedido.RequestUri = original;
            if (resposta.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                CookiesEmitidos.AddRange(cookies);
            }

            return resposta;
        }
    }

    [Fact]
    public async Task Primeiro_login_cria_o_usuario_com_o_email_em_minusculas_e_abre_a_sessao()
    {
        using var navegador = banco.NovoNavegador();
        var conta = ContaGoogle.Nova($"Nova.Pessoa.{Guid.NewGuid():N}@Teste.com");

        await navegador.EntrarComGoogleAsync(banco.Google, conta);

        var sessao = await navegador.Http.GetFromJsonAsync<SessaoNaResposta>("/api/auth/me", Cancelamento);
        Assert.Equal(new SessaoNaResposta(conta.Nome, conta.Email.ToLowerInvariant()), sessao);
    }

    [Theory]
    [InlineData("o'brien")]
    [InlineData("ana&bia")]
    [InlineData("vi!salao")]
    [InlineData("#vi")]
    [InlineData("vi~salao")]
    public async Task Primeiro_login_com_email_valido_de_caractere_incomum_cria_o_usuario(string inicioDoEmail)
    {
        using var navegador = banco.NovoNavegador();
        var conta = ContaGoogle.Nova($"{inicioDoEmail}.{Guid.NewGuid():N}@teste.com");

        var resposta = await navegador.EntrarComGoogleAsync(banco.Google, conta);

        Assert.Equal("/", resposta.Headers.Location?.OriginalString);
        var sessao = await navegador.Http.GetFromJsonAsync<SessaoNaResposta>("/api/auth/me", Cancelamento);
        Assert.Equal(conta.Email, sessao?.Email);
    }

    [Fact]
    public async Task Depois_do_login_volta_para_a_tela_de_onde_saiu()
    {
        using var navegador = banco.NovoNavegador();

        var resposta = await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(), returnUrl: "/agenda?dia=2026-10-01");

        Assert.Equal(HttpStatusCode.Redirect, resposta.StatusCode);
        Assert.Equal("/agenda?dia=2026-10-01", resposta.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Segundo_login_da_mesma_conta_entra_no_mesmo_usuario()
    {
        var conta = ContaGoogle.Nova();
        using (var primeiraVez = banco.NovoNavegador())
        {
            await primeiraVez.EntrarComGoogleAsync(banco.Google, conta);
        }
        using var segundaVez = banco.NovoNavegador();

        var resposta = await segundaVez.EntrarComGoogleAsync(banco.Google, conta);

        Assert.Equal("/", resposta.Headers.Location?.OriginalString);
        var sessao = await segundaVez.Http.GetFromJsonAsync<SessaoNaResposta>("/api/auth/me", Cancelamento);
        Assert.Equal(conta.Email, sessao?.Email);
    }

    [Fact]
    public async Task Login_de_email_ja_cadastrado_liga_ao_usuario_existente()
    {
        var email = CadastrosTests.EmailUnico();
        await UsuarioExistenteAsync(email, "Cliente do Salão");
        using var navegador = banco.NovoNavegador();

        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(email));

        var sessao = await navegador.Http.GetFromJsonAsync<SessaoNaResposta>("/api/auth/me", Cancelamento);
        Assert.Equal(new SessaoNaResposta("Cliente do Salão", email), sessao);
    }

    [Fact]
    public async Task Email_que_o_google_nao_verificou_nao_liga_a_ninguem_nem_abre_sessao()
    {
        var email = CadastrosTests.EmailUnico();
        await UsuarioExistenteAsync(email, "Dona do E-mail");
        using var navegador = banco.NovoNavegador();
        var conta = ContaGoogle.Nova(email, emailVerificado: false);

        var resposta = await navegador.EntrarComGoogleAsync(banco.Google, conta);

        Assert.Equal("/entrar?erro=email-nao-verificado", resposta.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);
        await using var escopo = banco.NovoEscopo(out var db);
        Assert.False(await db.UserLogins.AnyAsync(login => login.ProviderKey == conta.Id, Cancelamento));
    }

    [Fact]
    public async Task Outra_conta_google_com_o_email_de_quem_ja_entra_pelo_google_e_recusada()
    {
        var contaDeAntes = await ContaQueJaEntrouAsync();
        using var navegador = banco.NovoNavegador();
        var outraConta = ContaGoogle.Nova(contaDeAntes.Email);

        var resposta = await navegador.EntrarComGoogleAsync(banco.Google, outraConta);

        Assert.Equal("/entrar?erro=outra-conta-google", resposta.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);
        await using var escopo = banco.NovoEscopo(out var db);
        Assert.False(await db.UserLogins.AnyAsync(login => login.ProviderKey == outraConta.Id, Cancelamento));
    }

    [Fact]
    public async Task Email_nao_verificado_de_quem_ja_entra_pelo_google_nao_revela_que_o_email_tem_conta()
    {
        var contaDeAntes = await ContaQueJaEntrouAsync();
        using var navegador = banco.NovoNavegador();

        var resposta = await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(contaDeAntes.Email, emailVerificado: false));

        Assert.Equal("/entrar?erro=email-nao-verificado", resposta.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Email_novo_que_o_google_nao_verificou_nao_cria_usuario()
    {
        using var navegador = banco.NovoNavegador();
        var conta = ContaGoogle.Nova(emailVerificado: false);

        var resposta = await navegador.EntrarComGoogleAsync(banco.Google, conta);

        Assert.Equal("/entrar?erro=email-nao-verificado", resposta.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);
        await using var escopo = banco.NovoEscopo(out var db);
        Assert.False(await db.Users.AnyAsync(usuario => usuario.Email == conta.Email, Cancelamento));
    }

    [Fact]
    public async Task Atras_do_proxy_em_http_todo_cookie_que_a_api_emite_e_secure()
    {
        var proxy = new ProxyEmHttp();
        using var navegador = new NavegadorTests(banco.Api, proxy);

        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova());
        Assert.Equal(HttpStatusCode.OK, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);

        var cookies = proxy.CookiesEmitidos
            .Select(cookie => (
                Nome: cookie.Split('=')[0],
                Secure: cookie.Split(';').Any(atributo => atributo.Trim().Equals("secure", StringComparison.OrdinalIgnoreCase))))
            .ToList();
        var nomes = cookies.Select(cookie => cookie.Nome).ToList();
        Assert.Contains(NavegadorTests.CookieDaSessao, nomes);
        Assert.Contains(IdentityConstants.ExternalScheme, nomes);
        Assert.Contains(nomes, nome => nome.StartsWith(".AspNetCore.Antiforgery."));
        Assert.Contains("XSRF-TOKEN", nomes);
        Assert.Empty(cookies.Where(cookie => !cookie.Secure).Select(cookie => cookie.Nome));
    }

    [Fact]
    public async Task Sessao_e_persistente_por_30_dias()
    {
        using var navegador = banco.NovoNavegador();

        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova());

        var sessao = navegador.Cookies.GetCookies(NavegadorTests.Endereco)[NavegadorTests.CookieDaSessao];
        Assert.NotNull(sessao);
        Assert.InRange(sessao.Expires, DateTime.Now.AddDays(29), DateTime.Now.AddDays(31));
    }

    [Fact]
    public async Task Sem_sessao_a_rota_da_sessao_responde_401()
    {
        using var navegador = banco.NovoNavegador();

        var resposta = await navegador.Http.GetAsync("/api/auth/me", Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
