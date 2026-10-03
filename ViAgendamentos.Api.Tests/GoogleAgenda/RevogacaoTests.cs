using System.Net;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ViAgendamentos.Api.GoogleAgenda;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.GoogleAgenda;

public class RevogacaoTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static Task<Events> ListarAsync(CalendarService agenda) => agenda.Events.List("primary").ExecuteAsync(Cancelamento);

    private async Task<T> UsarAsync<T>(Guid profissionalId, Func<CalendarService, Task<T>> uso)
    {
        await using var escopo = banco.Api.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<AgendaGoogle>().UsarAsync(profissionalId, uso, Cancelamento);
    }

    private async Task<ConexaoGoogle> ConexaoAsync(Guid profissionalId)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        return await db.ConexoesGoogle.SingleAsync(c => c.ProfissionalId == profissionalId, Cancelamento);
    }

    [Fact]
    public async Task Uso_com_conexao_ativa_renova_o_acesso_e_le_a_agenda()
    {
        var conta = ContaDaAgenda.Nova();
        var profissional = await CadastrosTests.ProfissionalConectadaAsync(banco, conta);
        banco.GoogleAgenda.EventosDe(conta).Add(new Event { Id = "dentista", Summary = "Dentista" });

        var eventos = await UsarAsync(profissional.Id, ListarAsync);

        Assert.Equal("Dentista", Assert.Single(eventos.Items).Summary);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], banco.GoogleAgenda.ChamadasDe(conta).Select(c => c.Metodo));
    }

    [Fact]
    public async Task Token_revogado_no_google_marca_a_conexao_como_revogada_com_o_erro()
    {
        var conta = ContaDaAgenda.Nova();
        var profissional = await CadastrosTests.ProfissionalConectadaAsync(banco, conta);
        banco.GoogleAgenda.RevogarRefreshToken(conta);

        var erro = await Assert.ThrowsAsync<ConexaoGoogleIndisponivelException>(() => UsarAsync(profissional.Id, ListarAsync));

        Assert.Equal(SituacaoDaConexao.Revogada, erro.Situacao);
        var conexao = await ConexaoAsync(profissional.Id);
        Assert.Equal(StatusConexao.Revogada, conexao.Status);
        Assert.Equal("invalid_grant: Token has been expired or revoked.", conexao.UltimoErro);
    }

    [Fact]
    public async Task Falha_passageira_do_google_nao_revoga_a_conexao()
    {
        var conta = ContaDaAgenda.Nova();
        var profissional = await CadastrosTests.ProfissionalConectadaAsync(banco, conta);
        banco.GoogleAgenda.FalharToken(conta, HttpStatusCode.ServiceUnavailable);

        var erro = await Assert.ThrowsAsync<TokenResponseException>(() => UsarAsync(profissional.Id, ListarAsync));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, erro.StatusCode);
        Assert.Equal([HttpMethod.Post], banco.GoogleAgenda.ChamadasDe(conta).Select(c => c.Metodo));
        var conexao = await ConexaoAsync(profissional.Id);
        Assert.Equal(StatusConexao.Ativa, conexao.Status);
        Assert.Null(conexao.UltimoErro);
    }

    [Fact]
    public async Task Falha_passageira_da_agenda_sobe_sem_repetir_o_pedido()
    {
        var conta = ContaDaAgenda.Nova();
        var profissional = await CadastrosTests.ProfissionalConectadaAsync(banco, conta);
        banco.GoogleAgenda.FalharAgenda(conta, HttpMethod.Get, HttpStatusCode.ServiceUnavailable);

        var erro = await Assert.ThrowsAsync<GoogleApiException>(() => UsarAsync(profissional.Id, ListarAsync));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, erro.HttpStatusCode);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], banco.GoogleAgenda.ChamadasDe(conta).Select(c => c.Metodo));
    }

    [Fact]
    public async Task Uso_sem_conexao_avisa_que_a_agenda_nao_esta_conectada()
    {
        Guid profissionalId;
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            var salao = await CadastrosTests.SalaoAsync(db);
            profissionalId = (await CadastrosTests.ProfissionalAsync(db, salao.Id)).Id;
        }

        var erro = await Assert.ThrowsAsync<ConexaoGoogleIndisponivelException>(() => UsarAsync(profissionalId, ListarAsync));

        Assert.Equal(SituacaoDaConexao.Desconectada, erro.Situacao);
    }

    [Fact]
    public async Task Uso_com_conexao_revogada_nao_chama_o_google()
    {
        var conta = ContaDaAgenda.Nova();
        var profissional = await CadastrosTests.ProfissionalConectadaAsync(banco, conta);
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            var conexao = await db.ConexoesGoogle.SingleAsync(c => c.ProfissionalId == profissional.Id, Cancelamento);
            conexao.Revogar("invalid_grant: Token has been expired or revoked.");
            await db.SaveChangesAsync(Cancelamento);
        }

        var erro = await Assert.ThrowsAsync<ConexaoGoogleIndisponivelException>(() => UsarAsync(profissional.Id, ListarAsync));

        Assert.Equal(SituacaoDaConexao.Revogada, erro.Situacao);
        Assert.Empty(banco.GoogleAgenda.ChamadasDe(conta));
    }
}
