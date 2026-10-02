using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Health;

public class HealthTests
{
    // 18h em UTC, que são 15h em São Paulo.
    private static readonly DateTimeOffset AgoraUtc = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    private static WebApplicationFactory<Program> Fabrica(string ambiente, params (string Chave, string? Valor)[] ajustes) =>
        SubidaDaApiTests.Fabrica(ambiente, ajustes).WithWebHostBuilder(builder =>
            builder.ConfigureServices(servicos =>
            {
                var relogio = new FakeTimeProvider(AgoraUtc);
                // Fuso local diferente de UTC: se a rota usar a hora local, o teste de UTC falha.
                relogio.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
                servicos.AddSingleton<TimeProvider>(relogio);
            }));

    [Fact]
    public async Task Responde_nome_da_configuracao_ambiente_e_horario_em_utc()
    {
        using var fabrica = Fabrica("Development", ("App:Nome", "Agenda de Teste"));
        var cliente = fabrica.CreateClient();

        var json = await cliente.GetStringAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"aplicacao":"Agenda de Teste","ambiente":"Development","verificadoEm":"2026-09-29T18:00:00+00:00"}""",
            json);
    }

    [Fact]
    public async Task Em_producao_informa_o_ambiente_production()
    {
        using var fabrica = Fabrica("Production");
        var cliente = fabrica.CreateClient();

        var json = await cliente.GetStringAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Contains("\"ambiente\":\"Production\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Sem_nome_da_aplicacao_a_api_nao_sobe(string? nome)
    {
        var erro = SubidaDaApiTests.ErroAoSubir<OptionsValidationException>("Development", ("App:Nome", nome));

        Assert.Contains("Nome", erro.Message);
    }

    [Fact]
    public void Sem_string_de_conexao_a_api_nao_sobe()
    {
        var erro = SubidaDaApiTests.ErroAoSubir<OptionsValidationException>("Production", ("ConnectionStrings:Default", null));

        Assert.Contains("Default", erro.Message);
    }

    [Fact]
    public void Sem_as_credenciais_do_google_a_api_nao_sobe()
    {
        var erro = SubidaDaApiTests.ErroAoSubir<ArgumentException>("Production", ("Authentication:Google:ClientId", null));

        Assert.Equal("ClientId", erro.ParamName);
    }
}
