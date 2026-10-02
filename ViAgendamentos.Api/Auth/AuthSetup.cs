using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Auth;

public static class AuthSetup
{
    // O handler do Google não traz o email_verified do perfil, e sem ele não dá para confiar no e-mail.
    public const string EmailVerificado = "email_verified";

    public static IServiceCollection AddAuth(this IServiceCollection servicos)
    {
        // A sessão é o cookie do Identity. A volta do Google grava antes um cookie externo, que só vale para a requisição de callback, e o Identity troca pelo cookie da sessão
        servicos
            .AddAuthentication(autenticacao =>
            {
                autenticacao.DefaultScheme = IdentityConstants.ApplicationScheme; //é o cookie da sessão do Identity, o cookie principal que a pessoa leva depois de entrar.
                autenticacao.DefaultSignInScheme = IdentityConstants.ExternalScheme;//é o cookie temporário do Identity, que só vale para a requisição de callback do Google.
            })
            .AddGoogle(google =>
            {
                google.CallbackPath = "/api/signin-google";
                google.ClaimActions.MapJsonKey(EmailVerificado, "email_verified");
                google.Events.OnAccessDenied = contexto => VoltarParaEntrar(contexto, "cancelado");
                google.Events.OnRemoteFailure = contexto => VoltarParaEntrar(contexto, "google");
            })
            .AddIdentityCookies();
        // O ClientId e o ClientSecret vêm da configuração, e a validação do próprio handler roda na subida. Sem ela, a API subiria e toda requisição falharia.
        servicos
            .AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme)
            .BindConfiguration("Authentication:Google")
            .ValidateOnStart();

        servicos
            .AddIdentityCore<Usuario>(identity =>
            {
                identity.User.RequireUniqueEmail = true;
                // O nome de usuário é o e-mail, e a lista padrão do Identity recusaria e-mails válidos com ', &, !, # ou ~.
                identity.User.AllowedUserNameCharacters = string.Empty;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();
        servicos.AddScoped<LoginGoogle>();
        servicos.ConfigureApplicationCookie(cookie =>
        {
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
            // A API não tem tela de login. O .NET 10 já responde 401 nas rotas que devolvem JSON ou TypedResults, e isto vale para as demais, como uma rota que não existe ou uma que devolve string.
            cookie.Events.OnRedirectToLogin = contexto =>
            {
                contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });
        // O cookie externo nasce no handler do Google, antes do app.UseCookiePolicy, então o Secure vai nele mesmo.
        servicos.ConfigureExternalCookie(cookie => cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always);

        // As chaves que cifram o cookie ficam no banco, para toda instância da API abrir a sessão de qualquer outra, 
        // sem um nome fixo, o Data Protection separa as chaves pela pasta da API, e uma imagem instalada em outra pasta derrubaria todas as sessões.
        servicos.AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("vi-agendamentos");

        servicos.AddAntiforgery(antiforgery => antiforgery.HeaderName = "X-XSRF-TOKEN");

        // Todo cookie sai Secure, com o app.UseCookiePolicy: a API fica atrás de proxy e recebe a requisição em http e o SameAsRequest padrão tiraria o Secure
        servicos.Configure<CookiePolicyOptions>(politica => politica.Secure = CookieSecurePolicy.Always);

        // Toda rota exige sessão, salvo as marcadas com AllowAnonymous: rota nova esquecida responde 401 e não vaza nada.
        servicos.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return servicos;
    }
    private static Task VoltarParaEntrar(HandleRequestContext<RemoteAuthenticationOptions> contexto, string motivo)
    {
        contexto.Response.Redirect(AuthEndpoints.TelaDeEntrar(motivo));
        contexto.HandleResponse();
        return Task.CompletedTask;
    }
}
