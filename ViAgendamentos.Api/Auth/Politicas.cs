using Microsoft.AspNetCore.Authorization;

namespace ViAgendamentos.Api.Auth;

public static class Politicas
{
    public const string ProfissionalDoSalao = nameof(ProfissionalDoSalao);
    public const string PropriaProfissional = nameof(PropriaProfissional);

    public static AuthorizationBuilder AddPoliticasDoSalao(this AuthorizationBuilder autorizacao)
    {
        autorizacao.Services.AddScoped<IAuthorizationHandler, AuthHandler>();
        return autorizacao
            .AddPolicy(ProfissionalDoSalao, politica => politica
                .RequireAuthenticatedUser()
                .AddRequirements(new AuthRequirement(SoAPropriaProfissional: false)))
            .AddPolicy(PropriaProfissional, politica => politica
                .RequireAuthenticatedUser()
                .AddRequirements(new AuthRequirement(SoAPropriaProfissional: true)));
    }
}
