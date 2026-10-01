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
builder.Services
    .AddOptionsWithValidateOnStart<AppOptions>()
    .BindConfiguration(AppOptions.Secao)
    .ValidateDataAnnotations();
builder.Services
    .AddOptionsWithValidateOnStart<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Secao)
    .ValidateDataAnnotations();
builder.Services.AddDbContext<AppDbContext>((servicos, opcoes) => opcoes
    .UseNpgsql(
        servicos.GetRequiredService<IOptions<DatabaseOptions>>().Value.Default,
        npgsql => npgsql.MapEnum<Role>("role").MapEnum<Origem>("origem"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddAuth();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGroup("/api")
    .MapHealth()
    .MapAuth();
app.MapHealthChecks("/api/health/db").AllowAnonymous();

app.Run();
