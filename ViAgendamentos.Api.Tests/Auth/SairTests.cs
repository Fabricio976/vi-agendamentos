using System.Net;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Auth;

public class SairTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    // Como o front: entra e pede a sessão, que traz o token antifalsificação da pessoa logada.
    private async Task<NavegadorTests> LogadoAsync()
    {
        var navegador = banco.NovoNavegador();
        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova());
        Assert.Equal(HttpStatusCode.OK, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);
        return navegador;
    }

    [Fact]
    public async Task Sair_com_o_cabecalho_antifalsificacao_encerra_a_sessao()
    {
        using var navegador = await LogadoAsync();
        var sair = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        sair.Headers.Add("X-XSRF-TOKEN", navegador.Cookie("XSRF-TOKEN"));

        var resposta = await navegador.Http.SendAsync(sair, Cancelamento);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);
    }

    [Fact]
    public async Task Sair_sem_o_cabecalho_antifalsificacao_e_recusado_e_a_sessao_continua()
    {
        using var navegador = await LogadoAsync();

        var resposta = await navegador.Http.PostAsync("/api/auth/logout", null, Cancelamento);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);
    }

    // O .NET 10 já responde 401 nas rotas que devolvem TypedResults ou JSON, como a de sair. Rota sem endpoint
    // não tem essa marca, e é nela que o cookie redirecionaria para a tela de login que a API não tem.
    [Fact]
    public async Task Sem_sessao_rota_que_nao_existe_responde_401_e_nao_redireciona()
    {
        using var navegador = banco.NovoNavegador();

        var resposta = await navegador.Http.GetAsync("/api/rota-que-nao-existe", Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
