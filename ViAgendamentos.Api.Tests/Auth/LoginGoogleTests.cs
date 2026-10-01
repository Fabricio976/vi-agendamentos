using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ViAgendamentos.Api.Tests.Data;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Tests.Auth;

public class LoginGoogleTests(PostgresFixture banco)
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

    [Fact]
    public async Task Primeiro_login_cria_o_usuario_com_o_email_em_minusculas_e_abre_a_sessao()
    {
        using var navegador = banco.NovoNavegador();
        var conta = ContaGoogle.Nova($"Nova.Pessoa.{Guid.NewGuid():N}@Teste.com");

        await navegador.EntrarComGoogleAsync(banco.Google, conta);

        var sessao = await navegador.Http.GetFromJsonAsync<SessaoNaResposta>("/api/auth/me", Cancelamento);
        Assert.Equal(new SessaoNaResposta(conta.Nome, conta.Email.ToLowerInvariant()), sessao);
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
        var email = Cadastros.EmailUnico();
        await UsuarioExistenteAsync(email, "Cliente do Salão");
        using var navegador = banco.NovoNavegador();

        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(email));

        var sessao = await navegador.Http.GetFromJsonAsync<SessaoNaResposta>("/api/auth/me", Cancelamento);
        Assert.Equal(new SessaoNaResposta("Cliente do Salão", email), sessao);
    }

    [Fact]
    public async Task Email_que_o_google_nao_verificou_nao_liga_a_ninguem_nem_abre_sessao()
    {
        var email = Cadastros.EmailUnico();
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
    public async Task Sessao_e_persistente_por_30_dias()
    {
        using var navegador = banco.NovoNavegador();

        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova());

        var sessao = navegador.Cookies.GetCookies(Navegador.Endereco)[Navegador.CookieDaSessao];
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
