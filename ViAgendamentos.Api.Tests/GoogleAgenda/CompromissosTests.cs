using Google.Apis.Calendar.v3.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using ViAgendamentos.Api.Agenda;
using ViAgendamentos.Api.GoogleAgenda;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.GoogleAgenda;

public class CompromissosTests(PostgresFixtureTests banco)
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeZoneInfo Manaus = TimeZoneInfo.FindSystemTimeZoneById("America/Manaus");
    private static readonly TimeSpan MenosTres = TimeSpan.FromHours(-3);

    // 5 de outubro de 2026 inteiro, em São Paulo.
    private static readonly DateTimeOffset Inicio = new(2026, 10, 5, 0, 0, 0, MenosTres);
    private static readonly DateTimeOffset Fim = Inicio.AddDays(1);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static Intervalo Das(int hora) => new(Inicio.AddHours(hora), Inicio.AddHours(hora + 1));

    private static Event Evento(int hora, Action<Event>? ajuste = null)
    {
        var evento = new Event
        {
            Summary = $"Compromisso das {hora}h",
            Start = new EventDateTime { DateTimeDateTimeOffset = Inicio.AddHours(hora) },
            End = new EventDateTime { DateTimeDateTimeOffset = Inicio.AddHours(hora + 1) },
        };
        ajuste?.Invoke(evento);
        return evento;
    }

    private async Task<(ContaDaAgenda Conta, Guid ProfissionalId)> ConectadaAsync(params Event[] eventos)
    {
        var conta = ContaDaAgenda.Nova();
        var profissional = await CadastrosTests.ProfissionalConectadaAsync(banco, conta);
        banco.GoogleAgenda.EventosDe(conta).AddRange(eventos);
        return (conta, profissional.Id);
    }

    private async Task<IReadOnlyList<Intervalo>> OcupadosAsync(Guid profissionalId, TimeZoneInfo fuso)
    {
        await using var escopo = banco.Api.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<AgendaGoogle>()
            .OcupadosAsync(profissionalId, Inicio, Fim, fuso, Cancelamento);
    }

    [Fact]
    public async Task Evento_ocupado_ocupa_o_intervalo()
    {
        var (_, profissionalId) = await ConectadaAsync(Evento(14));

        Assert.Equal([Das(14)], await OcupadosAsync(profissionalId, SaoPaulo));
    }

    [Fact]
    public async Task Evento_marcado_como_disponivel_nao_ocupa()
    {
        var (_, profissionalId) = await ConectadaAsync(
            Evento(9, e => e.Transparency = "opaque"),
            Evento(11, e => e.Transparency = "transparent"));

        Assert.Equal([Das(9)], await OcupadosAsync(profissionalId, SaoPaulo));
    }

    [Fact]
    public async Task Evento_recusado_pela_profissional_nao_ocupa()
    {
        var (_, profissionalId) = await ConectadaAsync(
            Evento(9, e => e.Attendees =
            [
                new EventAttendee { Email = "vi@teste.com", Self = true, ResponseStatus = "accepted" },
                new EventAttendee { Email = "outra@teste.com", ResponseStatus = "declined" },
            ]),
            Evento(11, e => e.Attendees =
            [
                new EventAttendee { Email = "vi@teste.com", Self = true, ResponseStatus = "declined" },
                new EventAttendee { Email = "outra@teste.com", ResponseStatus = "accepted" },
            ]));

        Assert.Equal([Das(9)], await OcupadosAsync(profissionalId, SaoPaulo));
    }

    [Fact]
    public async Task Evento_criado_pelo_app_nao_ocupa()
    {
        var (_, profissionalId) = await ConectadaAsync(
            Evento(9, e => e.ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string> { [AgendaGoogle.PropriedadeDoApp] = AgendaGoogle.ValorDoApp },
            }),
            Evento(11, e => e.ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string> { [AgendaGoogle.PropriedadeDoApp] = "outro-app" },
            }));

        Assert.Equal([Das(11)], await OcupadosAsync(profissionalId, SaoPaulo));
    }

    // Manaus fica em −04: lido em UTC, o dia começaria 4 horas antes; com o fuso de São Paulo fixo no código, 1 hora antes.
    [Fact]
    public async Task Evento_de_dia_inteiro_bloqueia_o_dia_no_fuso_do_salao()
    {
        var (_, profissionalId) = await ConectadaAsync(new Event
        {
            Summary = "Folga",
            Start = new EventDateTime { Date = "2026-10-05" },
            End = new EventDateTime { Date = "2026-10-06" },
        });

        var ocupados = await OcupadosAsync(profissionalId, Manaus);

        var menosQuatro = TimeSpan.FromHours(-4);
        Assert.Equal(
            [new Intervalo(new DateTimeOffset(2026, 10, 5, 0, 0, 0, menosQuatro), new DateTimeOffset(2026, 10, 6, 0, 0, 0, menosQuatro))],
            ocupados);
    }

    [Fact]
    public async Task Compromissos_de_todas_as_paginas_ocupam()
    {
        var (_, profissionalId) = await ConectadaAsync(Evento(9), Evento(11), Evento(14));

        Assert.Equal([Das(9), Das(11), Das(14)], await OcupadosAsync(profissionalId, SaoPaulo));
    }

    [Fact]
    public async Task Lista_pede_recorrentes_expandidos_e_so_a_janela()
    {
        var (conta, profissionalId) = await ConectadaAsync();

        await OcupadosAsync(profissionalId, SaoPaulo);

        var lista = Assert.Single(banco.GoogleAgenda.ChamadasDe(conta), c => c.Endereco.Host == "www.googleapis.com");
        var parametros = QueryHelpers.ParseQuery(lista.Endereco.Query);
        Assert.Equal("true", parametros.GetValueOrDefault("singleEvents").ToString());
        Assert.Equal(Inicio, Instante(parametros.GetValueOrDefault("timeMin")));
        Assert.Equal(Fim, Instante(parametros.GetValueOrDefault("timeMax")));
    }

    private static DateTimeOffset? Instante(StringValues valor) =>
        DateTimeOffset.TryParse(valor.ToString(), out var instante) ? instante : null;
}
