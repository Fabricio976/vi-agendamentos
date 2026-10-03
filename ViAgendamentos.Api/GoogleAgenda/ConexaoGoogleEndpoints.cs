using System.Security.Claims;
using System.Text.Json;
using Google;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3.Data;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Auth;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.GoogleAgenda;

public sealed record ConexaoDaProfissional(SituacaoDaConexao Status, string? GoogleEmail, DateTimeOffset? ConectadoEm, string? UltimoErro);

public sealed record PassoDaVerificacao(bool Ok, string? Erro);

// Passo que não rodou fica nulo: depois de uma falha, só o apagar ainda roda.
public sealed record VerificacaoDaAgenda(
    PassoDaVerificacao Criado, PassoDaVerificacao? Alterado, PassoDaVerificacao? Listado, PassoDaVerificacao? Apagado);

public static class ConexaoGoogleEndpoints
{
    private const string Volta = "/google/agenda/callback";

    public static RouteGroupBuilder MapConexaoGoogle(this RouteGroupBuilder salao)
    {
        var google = salao.MapGroup("/profissionais/{profissionalId:guid}/google")
            .RequireAuthorization(Politicas.PropriaProfissional);

        google.MapGet("/", async (ProfissionalDaSessao profissional, AppDbContext db, CancellationToken cancelamento) =>
        {
            var conexao = await db.ConexoesGoogle.SingleOrDefaultAsync(c => c.ProfissionalId == profissional.Id, cancelamento);
            return new ConexaoDaProfissional(ConexaoGoogle.SituacaoDe(conexao), conexao?.GoogleEmail, conexao?.ConectadoEm, conexao?.UltimoErro);
        });

        google.MapGet("/conectar", (
            ProfissionalDaSessao profissional, ClaimsPrincipal principal, UserManager<Usuario> usuarios,
            GoogleAuthorizationCodeFlow fluxo, StateDaConexao states, HttpRequest requisicao) =>
        {
            var pedido = fluxo.CreateAuthorizationCodeRequest(EnderecoDeVolta(requisicao));
            pedido.State = states.Emitir(Guid.Parse(usuarios.GetUserId(principal)!), profissional.SalaoId, profissional.Id);
            return TypedResults.Redirect(pedido.Build().AbsoluteUri);
        });

        google.MapPost("/verificacao", VerificarAsync);

        return salao;
    }

    public static RouteGroupBuilder MapVoltaDoGoogleAgenda(this RouteGroupBuilder api)
    {
        api.MapGet(Volta, ConcluirAsync);
        return api;
    }

    // Montado pela requisição, como o retorno do login: no ambiente local, http://localhost:4200/api/google/agenda/callback.
    private static string EnderecoDeVolta(HttpRequest requisicao) =>
        UriHelper.BuildAbsolute(requisicao.Scheme, requisicao.Host, requisicao.PathBase, "/api" + Volta);

    private static string TelaDaConexao(Guid salaoId, ResultadoDaConexao resultado) =>
        $"/saloes/{salaoId}/google?resultado={JsonNamingPolicy.KebabCaseLower.ConvertName(resultado.ToString())}";

    // Só a sessão que pediu a conexão conclui. Com o state de outra pessoa, ou adulterado, nada é gravado,
    // e não há salão confiável para onde voltar.
    private static async Task<Results<RedirectHttpResult, ProblemHttpResult>> ConcluirAsync(
        string state, string? code, string? error, ClaimsPrincipal principal, UserManager<Usuario> usuarios,
        StateDaConexao states, VoltaDoGoogle volta, HttpRequest requisicao, CancellationToken cancelamento)
    {
        var pedido = states.Ler(state);
        if (pedido is null || pedido.UsuarioId != Guid.Parse(usuarios.GetUserId(principal)!))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "A volta do Google não pertence a esta sessão.");
        }

        if (states.Venceu(pedido))
        {
            return TypedResults.Redirect(TelaDaConexao(pedido.SalaoId, ResultadoDaConexao.Expirado));
        }

        // Sem código, o Google mandou error: access_denied é a pessoa desistindo na tela dele, e qualquer outro é falha.
        var resultado = code is null
            ? error == "access_denied" ? ResultadoDaConexao.Cancelada : ResultadoDaConexao.Erro
            : await volta.ConectarAsync(pedido.ProfissionalId, code, EnderecoDeVolta(requisicao), cancelamento);
        return TypedResults.Redirect(TelaDaConexao(pedido.SalaoId, resultado));
    }

    private static readonly TimeSpan DuracaoDaVerificacao = TimeSpan.FromMinutes(15);

    // Prova que o escopo basta para as quatro operações na agenda da profissional. O evento criado é sempre apagado,
    // mesmo quando um passo do meio falha: a verificação não deixa lixo na agenda dela.
    private static async Task<VerificacaoDaAgenda> VerificarAsync(
        ProfissionalDaSessao profissional, AgendaGoogle agenda, TimeProvider relogio, CancellationToken cancelamento)
    {
        var inicio = relogio.GetUtcNow().AddDays(1);
        string? eventoId = null;
        var criado = await PassoAsync(async () =>
            eventoId = await agenda.CriarEventoAsync(profissional.Id, EventoDaVerificacao(inicio), avisarConvidada: false, cancelamento));
        if (eventoId is null)
        {
            return new VerificacaoDaAgenda(criado, null, null, null);
        }

        var alterado = await PassoAsync(() => agenda.AlterarEventoAsync(
            profissional.Id, eventoId, new Event { Summary = "Verificação do Vi Agendamentos (alterada)" }, avisarConvidada: false, cancelamento));
        var listado = alterado.Ok ? await ListarAsync(agenda, profissional.Id, eventoId, inicio, cancelamento) : null;
        var apagado = await PassoAsync(() => agenda.ApagarEventoAsync(profissional.Id, eventoId, avisarConvidada: false, cancelamento));
        return new VerificacaoDaAgenda(criado, alterado, listado, apagado);
    }

    private static async Task<PassoDaVerificacao> ListarAsync(
        AgendaGoogle agenda, Guid profissionalId, string eventoId, DateTimeOffset inicio, CancellationToken cancelamento)
    {
        Events? eventos = null;
        var passo = await PassoAsync(async () => eventos = await agenda.UsarAsync(profissionalId, servico =>
        {
            var pedido = servico.Events.List("primary");
            pedido.TimeMinDateTimeOffset = inicio;
            pedido.TimeMaxDateTimeOffset = inicio + DuracaoDaVerificacao;
            return pedido.ExecuteAsync(cancelamento);
        }, cancelamento));
        return passo.Ok && !eventos!.Items.Any(evento => evento.Id == eventoId)
            ? new PassoDaVerificacao(false, "O evento criado não apareceu na lista da agenda.")
            : passo;
    }

    // Cada passo vira o resultado com a mensagem do erro do Google, é o que a verificação existe para mostrar.
    private static async Task<PassoDaVerificacao> PassoAsync(Func<Task> passo)
    {
        try
        {
            await passo();
            return new PassoDaVerificacao(true, null);
        }
        catch (Exception erro) when (erro is GoogleApiException or TokenResponseException or HttpRequestException or ConexaoGoogleIndisponivelException)
        {
            return new PassoDaVerificacao(false, erro.Message);
        }
    }
    private static Event EventoDaVerificacao(DateTimeOffset inicio) => new()
    {
        Summary = "Verificação do Vi Agendamentos",
        Start = new EventDateTime { DateTimeDateTimeOffset = inicio },
        End = new EventDateTime { DateTimeDateTimeOffset = inicio + DuracaoDaVerificacao },
        Transparency = "transparent",
        ExtendedProperties = new Event.ExtendedPropertiesData
        {
            Private__ = new Dictionary<string, string> { [AgendaGoogle.PropriedadeDoApp] = AgendaGoogle.ValorDoApp },
        },
    };
}
