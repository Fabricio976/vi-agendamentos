using ViAgendamentos.Api;
using ViAgendamentos.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddOptionsWithValidateOnStart<AppOptions>()
    .BindConfiguration(AppOptions.Secao)
    .ValidateDataAnnotations();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGroup("/api").MapHealth();

app.Run();
