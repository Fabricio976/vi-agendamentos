using Microsoft.Extensions.Options;

namespace ViAgendamentos.Api.Health;

public sealed record StatusDaApi(string Aplicacao, string Ambiente, DateTimeOffset VerificadoEm);

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealth(this RouteGroupBuilder api)
    {
        api.MapGet("/health", (IOptions<AppOptions> opcoes, IHostEnvironment ambiente, TimeProvider relogio) =>
                new StatusDaApi(opcoes.Value.Nome, ambiente.EnvironmentName, relogio.GetUtcNow()))
            .WithName("GetHealth");

        return api;
    }
}
