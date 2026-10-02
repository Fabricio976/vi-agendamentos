using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ViAgendamentos.Api;
using ViAgendamentos.Api.Auth;
using ViAgendamentos.Api.Clientes;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Health;
using ViAgendamentos.Api.Saloes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
// Enum sai como texto, com o mesmo rótulo do banco ("dona"), e não como número.
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services
    .AddOptionsWithValidateOnStart<AppOptions>()
    .BindConfiguration(AppOptions.Secao)
    .ValidateDataAnnotations();
builder.Services
    .AddOptionsWithValidateOnStart<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Secao)
    .ValidateDataAnnotations();
builder.Services
    .AddOptions<SalaoPilotoOptions>()
    .BindConfiguration(SalaoPilotoOptions.Secao)
    .ValidateDataAnnotations();
builder.Services.AddDbContext<AppDbContext>((servicos, opcoes) => opcoes
    .UseNpgsql(
        servicos.GetRequiredService<IOptions<DatabaseOptions>>().Value.Default,
        npgsql => npgsql.MapEnum<Role>("role").MapEnum<Origem>("origem"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddAuth();

var app = builder.Build();

if (args is [SalaoPiloto.Comando, ..])
{
    await SalaoPiloto.CadastrarAsync(app.Services);
    return;
}

app.UseCookiePolicy();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGroup("/api")
    .AddEndpointFilter(AuthEndpoints.ExigirAntifalsificacao)
    .MapHealth()
    .MapAuth()
    .MapSaloes();
app.MapHealthChecks("/api/health/db").AllowAnonymous();

app.Run();
