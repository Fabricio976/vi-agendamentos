using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.GoogleAgenda;

[assembly: AssemblyFixture(typeof(ViAgendamentos.Api.Tests.Data.PostgresFixtureTests))]

namespace ViAgendamentos.Api.Tests.Data;

// Um Postgres de verdade para a suíte inteira, na versão do compose e dos projetos novos do Supabase.
public sealed class PostgresFixtureTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplicationFactory<Program>? _api;

    // O Google da suíte: toda instância da API derivada desta fala com ele.
    public GoogleFalsoTests Google { get; } = new();

    // O Google Agenda da suíte, no lugar do handler do cliente nomeado que a biblioteca do Google usa.
    public GoogleAgendaFalsoTests GoogleAgenda { get; } = new();

    // A API com a configuração real, apontando para o contêiner dos testes.
    public WebApplicationFactory<Program> Api =>
        _api ?? throw new InvalidOperationException("O fixture ainda não subiu o banco.");

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuracao) =>
                configuracao.AddInMemoryCollection(ConfiguracaoDeTesteTests.Minima(_postgres.GetConnectionString())));
            builder.ConfigureTestServices(servicos =>
            {
                servicos.Configure<GoogleOptions>(
                    GoogleDefaults.AuthenticationScheme, google => google.BackchannelHttpHandler = Google);
                servicos.AddHttpClient(GoogleAgendaSetup.HttpClientDaAgenda).ConfigurePrimaryHttpMessageHandler(() => GoogleAgenda);
            });
        });

        await using var escopo = NovoEscopo(out var db);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    // A mesma API, com a configuração trocada, como outro banco.
    public WebApplicationFactory<Program> ApiCom(params (string Chave, string? Valor)[] ajustes) =>
        Api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuracao) =>
            configuracao.AddInMemoryCollection(ajustes.Select(ajuste => KeyValuePair.Create(ajuste.Chave, ajuste.Valor)))));

    // Um banco novo no mesmo Postgres. O MigrateAsync de quem usar cria o banco.
    public string ConexaoComOutroBanco() =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = $"outro_{Guid.NewGuid():N}" }.ConnectionString;

    public NavegadorTests NovoNavegador() => new(Api);

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
