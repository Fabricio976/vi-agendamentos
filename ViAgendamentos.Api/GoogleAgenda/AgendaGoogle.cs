using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Requests;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Agenda;
using ViAgendamentos.Api.Data;

namespace ViAgendamentos.Api.GoogleAgenda;

public sealed class ConexaoGoogleIndisponivelException(SituacaoDaConexao situacao, Exception? causa = null)
    : Exception($"A agenda Google da profissional está {situacao.ToString().ToLowerInvariant()}.", causa)
{
    public SituacaoDaConexao Situacao { get; } = situacao;
}

public sealed class AgendaGoogle(
    AppDbContext db, CifraDoToken cifra, GoogleAuthorizationCodeFlow fluxo, Google.Apis.Http.IHttpClientFactory http, ILogger<AgendaGoogle> log)
{
    public const string PropriedadeDoApp = "app";
    public const string ValorDoApp = "vi-agendamentos";

    private static readonly PageStreamer<Event, EventsResource.ListRequest, Events, string> Paginas =
        new((pedido, pagina) => pedido.PageToken = pagina, resposta => resposta.NextPageToken, resposta => resposta.Items);

    // Abre a agenda com um access token novo, renovado pelo refresh token do banco. Só o invalid_grant revoga a conexão:
    // é a resposta do Google ao token revogado ou vencido. Qualquer outra falha sobe como veio, e a conexão segue ativa.
    public async Task<T> UsarAsync<T>(Guid profissionalId, Func<CalendarService, Task<T>> uso, CancellationToken cancelamento)
    {
        var conexao = await db.ConexoesGoogle.SingleOrDefaultAsync(c => c.ProfissionalId == profissionalId, cancelamento);
        if (conexao?.Status is not StatusConexao.Ativa)
        {
            throw new ConexaoGoogleIndisponivelException(ConexaoGoogle.SituacaoDe(conexao));
        }

        var credencial = new UserCredential(
            fluxo, profissionalId.ToString(), new TokenResponse { RefreshToken = cifra.Decifrar(conexao.RefreshTokenCifrado) });
        using var agenda = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credencial,
            HttpClientFactory = http,
            DefaultExponentialBackOffPolicy = GoogleAgendaSetup.SemRetentativa,
            ApplicationName = "vi-agendamentos",
        });
        try
        {
            return await uso(agenda);
        }
        catch (TokenResponseException erro) when (erro.Error.Error == "invalid_grant")
        {
            conexao.Revogar($"{erro.Error.Error}: {erro.Error.ErrorDescription}");
            await db.SaveChangesAsync(cancelamento);
            log.LogWarning(erro, "O Google revogou a conexão da agenda da profissional {ProfissionalId}.", profissionalId);
            throw new ConexaoGoogleIndisponivelException(SituacaoDaConexao.Revogada, erro);
        }
    }

    // O Google devolve os eventos que cruzam a janela, com os recorrentes já expandidos em ocorrências.
    public Task<IReadOnlyList<Intervalo>> OcupadosAsync(
        Guid profissionalId, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso, CancellationToken cancelamento) =>
        UsarAsync<IReadOnlyList<Intervalo>>(profissionalId, async agenda =>
        {
            var pedido = agenda.Events.List("primary");
            pedido.SingleEvents = true;
            pedido.TimeMinDateTimeOffset = inicio;
            pedido.TimeMaxDateTimeOffset = fim;
            var eventos = await Paginas.FetchAllAsync(pedido, cancelamento);
            return [.. eventos.Where(Ocupa).Select(evento => IntervaloDe(evento, fuso))];
        }, cancelamento);

    public Task<string> CriarEventoAsync(Guid profissionalId, Event evento, bool avisarConvidada, CancellationToken cancelamento) =>
        UsarAsync(profissionalId, async agenda =>
        {
            var pedido = agenda.Events.Insert(evento, "primary");
            pedido.SendUpdates = Avisos<EventsResource.InsertRequest.SendUpdatesEnum>(avisarConvidada);
            return (await pedido.ExecuteAsync(cancelamento)).Id;
        }, cancelamento);

    public Task AlterarEventoAsync(Guid profissionalId, string eventoId, Event mudanca, bool avisarConvidada, CancellationToken cancelamento) =>
        UsarAsync(profissionalId, agenda =>
        {
            var pedido = agenda.Events.Patch(mudanca, "primary", eventoId);
            pedido.SendUpdates = Avisos<EventsResource.PatchRequest.SendUpdatesEnum>(avisarConvidada);
            return pedido.ExecuteAsync(cancelamento);
        }, cancelamento);

    public Task ApagarEventoAsync(Guid profissionalId, string eventoId, bool avisarConvidada, CancellationToken cancelamento) =>
        UsarAsync(profissionalId, agenda =>
        {
            var pedido = agenda.Events.Delete("primary", eventoId);
            pedido.SendUpdates = Avisos<EventsResource.DeleteRequest.SendUpdatesEnum>(avisarConvidada);
            return pedido.ExecuteAsync(cancelamento);
        }, cancelamento);

    // Cada pedido da biblioteca tem o próprio enum de sendUpdates, todos com All e None.
    private static TEnum Avisos<TEnum>(bool avisarConvidada) where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(avisarConvidada ? "All" : "None");

    // "Disponível" (transparent), recusado pela profissional ou criado por este app não ocupa:
    // o evento do app é o de um atendimento, que já está no banco.
    private static bool Ocupa(Event evento) =>
        evento.Transparency != "transparent"
        && evento.Attendees?.Any(convidado => convidado.Self == true && convidado.ResponseStatus == "declined") != true
        && !CriadoPeloApp(evento);

    private static bool CriadoPeloApp(Event evento) =>
        evento.ExtendedProperties?.Private__ is { } privadas
        && privadas.TryGetValue(PropriedadeDoApp, out var app)
        && app == ValorDoApp;

    private static Intervalo IntervaloDe(Event evento, TimeZoneInfo fuso) =>
        evento.Start.Date is null
            ? new(evento.Start.DateTimeDateTimeOffset!.Value.ToUniversalTime(), evento.End.DateTimeDateTimeOffset!.Value.ToUniversalTime())
            : new(MeiaNoite(evento.Start.Date, fuso), MeiaNoite(evento.End.Date, fuso));

    // O evento de dia inteiro traz só a data. A meia-noite dela é a do fuso do salão, e não a de UTC.
    private static DateTimeOffset MeiaNoite(string data, TimeZoneInfo fuso)
    {
        var meiaNoite = DateOnly.ParseExact(data, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(meiaNoite, fuso.GetUtcOffset(meiaNoite)).ToUniversalTime();
    }
}

