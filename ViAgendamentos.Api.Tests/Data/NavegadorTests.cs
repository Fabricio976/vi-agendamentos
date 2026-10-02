using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.WebUtilities;

namespace ViAgendamentos.Api.Tests.Data;

// Um navegador de teste: guarda os cookies e não segue redirecionamento, para o teste ver cada passo.
// Fala https porque os cookies do login são Secure e não viajam em http.
public sealed class NavegadorTests : IDisposable
{
    public static readonly Uri Endereco = new("https://localhost");
    public static readonly string CookieDaSessao = $".AspNetCore.{IdentityConstants.ApplicationScheme}";

    // Os intermediários ficam entre os cookies do navegador e a API, como um proxy no caminho.
    public NavegadorTests(WebApplicationFactory<Program> api, params DelegatingHandler[] intermediarios) =>
        Http = api.CreateDefaultClient(Endereco, [new CookieContainerHandler(Cookies), .. intermediarios]);

    public CookieContainer Cookies { get; } = new();
    public HttpClient Http { get; }

    public string? Cookie(string nome) => Cookies.GetCookies(Endereco)[nome]?.Value;

    // Leva a sessão aberta em outro navegador, como o mesmo celular falando com outra instância da API.
    public void CopiarSessaoDe(NavegadorTests outro) =>
        Cookies.Add(Endereco, new Cookie(CookieDaSessao, outro.Cookie(CookieDaSessao)));

    // Pede o login à API e devolve o state que ela mandou ao Google, que o Google devolve na volta.
    public async Task<string> IrAoGoogleAsync(string returnUrl = "/")
    {
        var ida = await Http.GetAsync($"/api/auth/google?returnUrl={Uri.EscapeDataString(returnUrl)}", Cancelamento);
        Assert.Equal(HttpStatusCode.Redirect, ida.StatusCode);
        return QueryHelpers.ParseQuery(ida.Headers.Location!.Query)["state"].ToString();
    }

    // O caminho inteiro: a API manda ao Google, o Google volta com o código e a API conclui o login.
    // Devolve a última resposta da API, o redirecionamento para a tela do front.
    public async Task<HttpResponseMessage> EntrarComGoogleAsync(GoogleFalsoTests google, ContaGoogle conta, string returnUrl = "/")
    {
        var state = await IrAoGoogleAsync(returnUrl);
        var volta = await Http.GetAsync(
            $"/api/signin-google?code={google.CodigoPara(conta)}&state={Uri.EscapeDataString(state)}", Cancelamento);
        Assert.Equal(HttpStatusCode.Redirect, volta.StatusCode);
        return await Http.GetAsync(volta.Headers.Location, Cancelamento);
    }

    // Pede a conexão da agenda à API e devolve o state que ela mandou ao Google.
    public async Task<string> IrAoGoogleAgendaAsync(Guid salaoId, Guid profissionalId)
    {
        var ida = await Http.GetAsync($"/api/saloes/{salaoId}/profissionais/{profissionalId}/google/conectar", Cancelamento);
        Assert.Equal(HttpStatusCode.Redirect, ida.StatusCode);
        return QueryHelpers.ParseQuery(ida.Headers.Location!.Query)["state"].ToString();
    }

    public Task<HttpResponseMessage> VoltarDoGoogleAgendaAsync(string query) =>
        Http.GetAsync($"/api/google/agenda/callback?{query}", Cancelamento);

    // O caminho inteiro da conexão. Devolve a volta da API, o redirecionamento para a tela da conexão.
    public async Task<HttpResponseMessage> ConectarGoogleAgendaAsync(GoogleAgendaFalsoTests google, Guid salaoId, Guid profissionalId, ContaDaAgenda conta)
    {
        var state = await IrAoGoogleAgendaAsync(salaoId, profissionalId);
        return await VoltarDoGoogleAgendaAsync($"code={google.CodigoPara(conta)}&state={Uri.EscapeDataString(state)}");
    }

    public void Dispose() => Http.Dispose();

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;
}
