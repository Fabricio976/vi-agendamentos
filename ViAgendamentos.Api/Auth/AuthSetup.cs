using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
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
        // A sessão é o cookie do Identity. A volta do Google grava antes um cookie externo, que só vale
        // até o callback decidir quem é a pessoa. Sem um padrão explícito, o Google seria o único esquema
        // e viraria o padrão, e o handler remoto encaminharia para si mesmo até estourar a pilha.
        servicos
            .AddAuthentication(autenticacao =>
            {
                autenticacao.DefaultScheme = IdentityConstants.ApplicationScheme;
                autenticacao.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddGoogle(google =>
            {
                google.CallbackPath = "/api/signin-google";
                google.ClaimActions.MapJsonKey(EmailVerificado, "email_verified");
            })
            .AddIdentityCookies();
        // O ClientId e o ClientSecret vêm da configuração, e a validação do próprio handler roda na subida:
        // sem ela, a API subiria e toda requisição falharia.
        servicos
            .AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme)
            .BindConfiguration("Authentication:Google")
            .ValidateOnStart();

        servicos
            .AddIdentityCore<Usuario>(identity => identity.User.RequireUniqueEmail = true)
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();
        servicos.ConfigureApplicationCookie(cookie =>
        {
            // Secure sempre: em produção a API fica atrás de proxy e recebe a requisição em http.
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            // 30 dias deslizantes, e não os 14 padrão: a cliente volta ao salão a cada 3 ou 4 semanas e, com 14,
            // entraria de novo a cada visita.
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
        });

        // Toda rota exige sessão, salvo as marcadas com AllowAnonymous: rota nova esquecida responde 401 e não vaza nada.
        servicos.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return servicos;
    }
}
