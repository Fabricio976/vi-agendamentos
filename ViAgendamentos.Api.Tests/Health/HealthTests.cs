using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Health;

public class HealthTests
{
    // 18h em UTC, que são 15h em São Paulo.
    private static readonly DateTimeOffset AgoraUtc = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    // A rota de health não abre o banco, mas a API exige a string de conexão para subir.
    private const string ConexaoNaoUsada = "Host=localhost;Database=nao_usado";

    private static WebApplicationFactory<Program> Fabrica(string ambiente, params (string Chave, string? Valor)[] ajustes) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(ambiente);

            builder.ConfigureAppConfiguration((_, configuracao) =>
            {
                var valores = ConfiguracaoDeTesteTests.Minima(ConexaoNaoUsada);
                foreach (var (chave, valor) in ajustes)
                {
                    valores[chave] = valor;
                }

                configuracao.AddInMemoryCollection(valores);
            });

            builder.ConfigureServices(servicos =>
            {
                var relogio = new FakeTimeProvider(AgoraUtc);
                // Fuso local diferente de UTC: se a rota usar a hora local, o teste de UTC falha.
                relogio.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
                servicos.AddSingleton<TimeProvider>(relogio);
            });
        });

    // O WebApplicationFactory tem uma corrida quando a API falha ao subir: se o host se descarta antes do
    // StartAsync do teste, chega ObjectDisposedException no lugar do erro real (DeferredHostBuilder no .NET 10).
    // O host registra o erro real no log antes de se descartar, então o teste lê de lá.
    private static TErro ErroAoSubir<TErro>(string ambiente, params (string Chave, string? Valor)[] ajustes) where TErro : Exception
    {
        var log = new FakeLoggerProvider();
        using var fabrica = Fabrica(ambiente, ajustes);
        using var fabricaComLog = fabrica.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(log)));

        Assert.ThrowsAny<Exception>(() => fabricaComLog.CreateClient());

        var falha = Assert.Single(log.Collector.GetSnapshot(), registro => registro.Exception is not null);
        return Assert.IsAssignableFrom<TErro>(falha.Exception);
    }

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
        var erro = ErroAoSubir<OptionsValidationException>("Development", ("App:Nome", nome));

        Assert.Contains("Nome", erro.Message);
    }

    [Fact]
    public void Sem_string_de_conexao_a_api_nao_sobe()
    {
        var erro = ErroAoSubir<OptionsValidationException>("Production", ("ConnectionStrings:Default", null));

        Assert.Contains("Default", erro.Message);
    }

    [Fact]
    public void Sem_as_credenciais_do_google_a_api_nao_sobe()
    {
        var erro = ErroAoSubir<ArgumentException>("Production", ("Authentication:Google:ClientId", null));

        Assert.Equal("ClientId", erro.ParamName);
    }
}
