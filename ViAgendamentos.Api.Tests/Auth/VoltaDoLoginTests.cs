using System.Net;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Auth;

public class VoltaDoLoginTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://site-falso.com/entrar")]
    [InlineData("//site-falso.com")]
    [InlineData("/\\site-falso.com")]
    public async Task Endereco_de_volta_fora_do_app_e_recusado_antes_do_google(string returnUrl)
    {
        using var navegador = banco.NovoNavegador();

        var resposta = await navegador.Http.GetAsync($"/api/auth/google?returnUrl={Uri.EscapeDataString(returnUrl)}", Cancelamento);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("returnUrl", await resposta.Content.ReadAsStringAsync(Cancelamento));
    }

    [Fact]
    public async Task Desistir_no_google_volta_para_entrar_sem_sessao()
    {
        using var navegador = banco.NovoNavegador();
        var state = await navegador.IrAoGoogleAsync();

        var volta = await navegador.Http.GetAsync(
            $"/api/signin-google?error=access_denied&state={Uri.EscapeDataString(state)}", Cancelamento);

        Assert.Equal("/entrar?erro=cancelado", volta.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.Http.GetAsync("/api/auth/me", Cancelamento)).StatusCode);
    }

    [Fact]
    public async Task Volta_do_google_com_state_invalido_leva_para_entrar_com_o_motivo()
    {
        using var navegador = banco.NovoNavegador();

        var volta = await navegador.Http.GetAsync("/api/signin-google?code=qualquer&state=adulterado", Cancelamento);

        Assert.Equal("/entrar?erro=google", volta.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Callback_sem_passar_pelo_google_leva_para_entrar_com_o_motivo()
    {
        using var navegador = banco.NovoNavegador();

        var resposta = await navegador.Http.GetAsync("/api/auth/google/callback?returnUrl=%2F", Cancelamento);

        Assert.Equal("/entrar?erro=google", resposta.Headers.Location?.OriginalString);
    }
}
