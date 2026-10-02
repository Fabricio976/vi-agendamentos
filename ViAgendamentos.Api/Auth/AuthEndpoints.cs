using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Auth;

public sealed record Sessao(string Nome, string Email);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapGet("/google", Results<ChallengeHttpResult, ValidationProblem> (string? returnUrl, SignInManager<Usuario> login) =>
            {
                var volta = returnUrl ?? "/";
                // Só telas do próprio app, com outro endereço, o link de login levaria a pessoa, já logada, a um site falso.
                if (!RedirectHttpResult.IsLocalUrl(volta))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["returnUrl"] = ["O endereço de volta precisa ser uma tela do próprio app."],
                    });
                }

                return TypedResults.Challenge(
                    login.ConfigureExternalAuthenticationProperties(
                        GoogleDefaults.AuthenticationScheme,
                        $"/api/auth/google/callback?returnUrl={Uri.EscapeDataString(volta)}"),
                    [GoogleDefaults.AuthenticationScheme]);
            })
            .AllowAnonymous();

        auth.MapGet("/google/callback", ConcluirLoginAsync).AllowAnonymous();

        auth.MapGet("/me", async (ClaimsPrincipal principal, UserManager<Usuario> usuarios, IAntiforgery antiforgery, HttpContext http) =>
        {
            var usuario = await usuarios.GetUserAsync(principal)
                ?? throw new InvalidOperationException("A sessão aponta para um usuário que não existe.");

            // O front pede a sessão ao abrir, inclusive logo depois do login, então o token sai ligado à pessoa logada
            // o Angular lê este cookie e devolve o valor no cabeçalho X-XSRF-TOKEN, por isso ele não é HttpOnly.
            var tokens = antiforgery.GetAndStoreTokens(http);
            http.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
            });

            return TypedResults.Ok(new Sessao(usuario.Nome, usuario.Email!));
        });

        auth.MapPost("/logout", async (SignInManager<Usuario> login) =>
        {
            await login.SignOutAsync();
            return TypedResults.NoContent();
        });

        return api;
    }

    // O antiforgery do ASP.NET Core só confere sozinho as rotas que recebem formulário as de JSON passam aqui
    // GET, HEAD, OPTIONS e TRACE passam direto, porque não mudam dado
    public static async ValueTask<object?> ExigirAntifalsificacao(EndpointFilterInvocationContext contexto, EndpointFilterDelegate proximo)
    {
        var antiforgery = contexto.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        return await antiforgery.IsRequestValidAsync(contexto.HttpContext)
            ? await proximo(contexto)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Falta o cabeçalho antifalsificação, ou ele não vale para esta sessão.");
    }

    // A tela de entrar do front, com o motivo de a entrada não ter acontecido.
    public static string TelaDeEntrar(string motivo) => $"/entrar?erro={motivo}";

    // Roda depois do handler do Google, que já trocou o código pelo perfil e gravou o cookie externo.
    private static async Task<RedirectHttpResult> ConcluirLoginAsync(
        string returnUrl, HttpContext http, SignInManager<Usuario> login, LoginGoogle loginGoogle)
    {
        var externo = await login.GetExternalLoginInfoAsync();
        if (externo is null)
        {
            // Cookie externo vencido ou ausente, como quando alguém abre este endereço sem vir do Google
            return TypedResults.Redirect(TelaDeEntrar("google"));
        }

        var resultado = await loginGoogle.IdentificarAsync(externo);
        if (resultado is not Entrou(var usuario))
        {
            return await RecusarAsync(http, ((Recusado)resultado).Motivo);
        }

        // o app instalado não pede login a cada abertura.
        await login.SignInAsync(usuario, isPersistent: true, externo.LoginProvider);
        await http.SignOutAsync(IdentityConstants.ExternalScheme);
        return TypedResults.LocalRedirect(returnUrl);
    }

    // Volta para a tela de entrar com o motivo, sem sessão e sem o cookie externo do Google.
    private static async Task<RedirectHttpResult> RecusarAsync(HttpContext http, string motivo)
    {
        await http.SignOutAsync(IdentityConstants.ExternalScheme);
        return TypedResults.Redirect(TelaDeEntrar(motivo));
    }
}
