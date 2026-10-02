using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ViAgendamentos.Api.Tests.Data;

// A API montada sem o banco dos testes, para conferir a subida.
public static class SubidaDaApiTests
{
    // As rotas que esses testes chamam não abrem o banco, mas a API exige a string de conexão para subir.
    private const string ConexaoNaoUsada = "Host=localhost;Database=nao_usado";

    public static WebApplicationFactory<Program> Fabrica(string ambiente, params (string Chave, string? Valor)[] ajustes) =>
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
        });

    // O WebApplicationFactory tem uma corrida quando a API falha ao subir: se o host se descarta antes do
    // StartAsync do teste, chega ObjectDisposedException no lugar do erro real (DeferredHostBuilder no .NET 10).
    // O host registra o erro real no log antes de se descartar, então o teste lê de lá.
    public static TErro ErroAoSubir<TErro>(string ambiente, params (string Chave, string? Valor)[] ajustes) where TErro : Exception
    {
        var log = new FakeLoggerProvider();
        using var fabrica = Fabrica(ambiente, ajustes);
        using var fabricaComLog = fabrica.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(log)));

        Assert.ThrowsAny<Exception>(() => fabricaComLog.CreateClient());

        var falha = Assert.Single(log.Collector.GetSnapshot(), registro => registro.Exception is not null);
        return Assert.IsAssignableFrom<TErro>(falha.Exception);
    }
}
