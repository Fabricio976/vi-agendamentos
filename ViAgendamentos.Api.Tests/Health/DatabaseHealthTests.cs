using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Health;

public class DatabaseHealthTests(PostgresFixture banco)
{
    [Fact]
    public async Task Com_o_banco_no_ar_responde_healthy()
    {
        var cliente = banco.Api.CreateClient();

        var resposta = await cliente.GetAsync("/api/health/db", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Com_o_banco_fora_do_ar_responde_503_unhealthy()
    {
        // A mesma API do teste acima, só com outro endereço de banco: a porta 1 desta máquina não tem
        // ninguém escutando, então a conexão é recusada na hora.
        using var fabrica = banco.Api.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuracao) =>
                configuracao.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x",
                })));
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.GetAsync("/api/health/db", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal("Unhealthy", await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
