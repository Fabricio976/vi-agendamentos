using System.Security.Claims;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Flows;
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
}
