using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using ViAgendamentos.Api.Data;

[assembly: AssemblyFixture(typeof(ViAgendamentos.Api.Tests.Data.PostgresFixture))]

namespace ViAgendamentos.Api.Tests.Data;

// Um Postgres de verdade para a suíte inteira, na versão do compose e dos projetos novos do Supabase.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplicationFactory<Program>? _api;

    // A API com a configuração real, apontando para o contêiner dos testes.
    public WebApplicationFactory<Program> Api =>
        _api ?? throw new InvalidOperationException("O fixture ainda não subiu o banco.");

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuracao) =>
                configuracao.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                })));

        await using var escopo = NovoEscopo(out var db);
        await db.Database.MigrateAsync();
    }

    // Um escopo por uso, como uma requisição da API. Quem chama descarta o escopo.
    public AsyncServiceScope NovoEscopo(out AppDbContext db)
    {
        var escopo = Api.Services.CreateAsyncScope();
        db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        return escopo;
    }

    public async Task<List<string>> ColunasAsync(string tabela)
    {
        await using var escopo = NovoEscopo(out var db);
        return await db.Database
            .SqlQuery<string>($"select column_name::text from information_schema.columns where table_name = {tabela} order by 1")
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}
