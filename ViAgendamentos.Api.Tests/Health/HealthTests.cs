using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ViAgendamentos.Api.Tests.Health;

public class HealthTests
{
    // 18h em UTC, que são 15h em São Paulo.
    private static readonly DateTimeOffset AgoraUtc = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    private static WebApplicationFactory<Program> Fabrica(string ambiente, string? nomeDaAplicacao = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(ambiente);

            if (nomeDaAplicacao is not null)
            {
                builder.ConfigureAppConfiguration((_, configuracao) =>
                    configuracao.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["App:Nome"] = nomeDaAplicacao,
                    }));
            }

            builder.ConfigureServices(servicos =>
            {
                var relogio = new FakeTimeProvider(AgoraUtc);
                // Fuso local diferente de UTC: se a rota usar a hora local, o teste de UTC falha.
                relogio.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
                servicos.AddSingleton<TimeProvider>(relogio);
            });
        });

    // Pega: rota ausente, nome escrito no código em vez de lido da configuração e horário em hora local.
    [Fact]
    public async Task Responde_nome_da_configuracao_ambiente_e_horario_em_utc()
    {
        using var fabrica = Fabrica("Development", nomeDaAplicacao: "Agenda de Teste");
        var cliente = fabrica.CreateClient();

        var json = await cliente.GetStringAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"aplicacao":"Agenda de Teste","ambiente":"Development","verificadoEm":"2026-09-29T18:00:00+00:00"}""",
            json);
    }

    // Pega: ambiente escrito à mão como "Development".
    [Fact]
    public async Task Em_producao_informa_o_ambiente_production()
    {
        using var fabrica = Fabrica("Production");
        var cliente = fabrica.CreateClient();

        var json = await cliente.GetStringAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Contains("\"ambiente\":\"Production\"", json);
    }

    // Pega: validação da configuração removida, que deixaria a API subir com nome vazio.
    [Fact]
    public void Sem_nome_da_aplicacao_a_api_nao_sobe()
    {
        using var fabrica = Fabrica("Development", nomeDaAplicacao: "");

        var erro = Record.Exception(() => fabrica.CreateClient());

        var validacao = Assert.IsType<OptionsValidationException>(erro?.GetBaseException());
        Assert.Contains("Nome", validacao.Message);
    }
}
