using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using ViAgendamentos.Api.Pessoas;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Auth;

public sealed record Sessao(string Nome, string Email);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapGet("/google", (string? returnUrl, SignInManager<Usuario> login) =>
                TypedResults.Challenge(
                    login.ConfigureExternalAuthenticationProperties(
                        GoogleDefaults.AuthenticationScheme,
                        $"/api/auth/google/callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}"),
                    [GoogleDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        auth.MapGet("/google/callback", ConcluirLoginAsync).AllowAnonymous();

        auth.MapGet("/me", async (ClaimsPrincipal principal, UserManager<Usuario> usuarios) =>
        {
            var usuario = await usuarios.GetUserAsync(principal)
                ?? throw new InvalidOperationException("A sessão aponta para um usuário que não existe.");
            return TypedResults.Ok(new Sessao(usuario.Nome, usuario.Email!));
        });

        return api;
    }

    // A tela de entrar do front, com o motivo de a entrada não ter acontecido.
    public static string TelaDeEntrar(string motivo) => $"/entrar?erro={motivo}";

    // Roda depois do handler do Google, que já trocou o código pelo perfil e gravou o cookie externo.
    private static async Task<RedirectHttpResult> ConcluirLoginAsync(
        string returnUrl, HttpContext http, SignInManager<Usuario> login, UserManager<Usuario> usuarios)
    {
        var externo = await login.GetExternalLoginInfoAsync()
            ?? throw new InvalidOperationException("A volta do login chegou sem passar pelo Google.");
        var usuario = await usuarios.FindByLoginAsync(externo.LoginProvider, externo.ProviderKey)
            ?? await CadastrarAsync(externo, usuarios);
        if (usuario is null)
        {
            await http.SignOutAsync(IdentityConstants.ExternalScheme);
            return TypedResults.Redirect(TelaDeEntrar("email-nao-verificado"));
        }

        // Persistente: o app instalado não pede login a cada abertura.
        await login.SignInAsync(usuario, isPersistent: true, externo.LoginProvider);
        await http.SignOutAsync(IdentityConstants.ExternalScheme);
        return TypedResults.LocalRedirect(returnUrl);
    }

    // Primeira entrada desta conta Google: liga ao usuário do mesmo e-mail ou cria um.
    // Devolve null quando o Google não confirmou o e-mail, que então pode ser de outra pessoa.
    private static async Task<Usuario?> CadastrarAsync(ExternalLoginInfo externo, UserManager<Usuario> usuarios)
    {
        var email = externo.Principal.FindFirstValue(ClaimTypes.Email);
        var verificado = bool.TryParse(externo.Principal.FindFirstValue(AuthSetup.EmailVerificado), out var valor) && valor;
        if (email is null || !verificado)
        {
            return null;
        }

        email = Pessoa.NormalizarEmail(email);
        var usuario = await usuarios.FindByEmailAsync(email);
        if (usuario is null)
        {
            usuario = new Usuario
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                // O perfil do Google pode vir sem nome; aí o e-mail faz as vezes de nome.
                Nome = externo.Principal.FindFirstValue(ClaimTypes.Name) ?? email,
            };
            Garantir(await usuarios.CreateAsync(usuario));
        }

        Garantir(await usuarios.AddLoginAsync(usuario, externo));
        return usuario;
    }

    private static void Garantir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", resultado.Errors.Select(erro => erro.Description)));
        }
    }
}
