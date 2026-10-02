using System.Buffers.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Data;

namespace ViAgendamentos.Api.GoogleAgenda;

// O que a volta do Google conta à tela da conexão, no parâmetro resultado.
public enum ResultadoDaConexao
{
    Conectada,
    Cancelada,
    Expirado,
    SemPermissao,
    Erro,
}

// Troca o código da volta do Google pelos tokens e grava a conexão. Só o refresh token fica: o access token vence
// em uma hora, e cada uso pede outro.
public sealed class VoltaDoGoogle(
    GoogleAuthorizationCodeFlow fluxo, CifraDoToken cifra, AppDbContext db, TimeProvider relogio, ILogger<VoltaDoGoogle> log)
{
    public async Task<ResultadoDaConexao> ConectarAsync(Guid profissionalId, string codigo, string enderecoDeVolta, CancellationToken cancelamento)
    {
        TokenResponse token;
        try
        {
            token = await fluxo.ExchangeCodeForTokenAsync(profissionalId.ToString(), codigo, enderecoDeVolta, cancelamento);
        }
        catch (TokenResponseException erro)
        {
            log.LogError(erro, "O Google recusou o código da conexão da profissional {ProfissionalId}.", profissionalId);
            return ResultadoDaConexao.Erro;
        }

        if (token.RefreshToken is null)
        {
            log.LogError("O Google conectou a agenda da profissional {ProfissionalId} sem refresh token.", profissionalId);
            return ResultadoDaConexao.Erro;
        }

        // A tela de consentimento do Google deixa a pessoa desmarcar a permissão da agenda, e o token volta sem ela.
        if (token.Scope?.Split(' ').Contains(CalendarService.Scope.CalendarEventsOwned) != true)
        {
            log.LogWarning("A profissional {ProfissionalId} conectou o Google sem a permissão da agenda.", profissionalId);
            return ResultadoDaConexao.SemPermissao;
        }

        var conexao = await db.ConexoesGoogle.SingleOrDefaultAsync(c => c.ProfissionalId == profissionalId, cancelamento);
        if (conexao is null)
        {
            conexao = new ConexaoGoogle { ProfissionalId = profissionalId };
            db.ConexoesGoogle.Add(conexao);
        }

        conexao.Conectar(EmailDoIdToken(token.IdToken), cifra.Cifrar(token.RefreshToken), relogio.GetUtcNow());
        await db.SaveChangesAsync(cancelamento);
        return ResultadoDaConexao.Conectada;
    }

    // O id token chega direto do endpoint de token do Google, por TLS e nesse caso a OpenID Connect dispensa conferir a assinatura.
    private static string EmailDoIdToken(string idToken)
    {
        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(idToken.Split('.')[1]));
        return payload.RootElement.GetProperty("email").GetString()
            ?? throw new InvalidOperationException("O id token do Google veio sem e-mail.");
    }
}
