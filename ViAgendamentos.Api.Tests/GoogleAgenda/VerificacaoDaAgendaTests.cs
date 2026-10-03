using System.Net;
using System.Net.Http.Json;
using Google.Apis.Calendar.v3.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ViAgendamentos.Api.GoogleAgenda;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.GoogleAgenda;

internal sealed record PassoNaResposta(bool Ok, string? Erro);

internal sealed record VerificacaoNaResposta(PassoNaResposta Criado, PassoNaResposta? Alterado, PassoNaResposta? Listado, PassoNaResposta? Apagado);

public class VerificacaoDaAgendaTests(PostgresFixtureTests banco)
{
    private static readonly PassoNaResposta Ok = new(true, null);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task<ContaDaAgenda> ConectarAsync(ProfissionalLogada logada)
    {
        var conta = ContaDaAgenda.Nova();
        await logada.Navegador.ConectarGoogleAgendaAsync(banco.GoogleAgenda, logada.Salao.Id, logada.Profissional.Id, conta);
        return conta;
    }

    private IEnumerable<ChamadaAoGoogle> ChamadasAAgenda(ContaDaAgenda conta) =>
        banco.GoogleAgenda.ChamadasDe(conta).Where(c => c.Endereco.Host == "www.googleapis.com");

    private static string SendUpdates(ChamadaAoGoogle chamada) =>
        QueryHelpers.ParseQuery(chamada.Endereco.Query).GetValueOrDefault("sendUpdates").ToString();

    [Fact]
    public async Task Verificacao_da_agenda_cria_altera_lista_e_apaga_nessa_ordem()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var conta = await ConectarAsync(logada);

        var resposta = await logada.Navegador.PostarAsync($"{logada.Rota}/google/verificacao");

        Assert.Equal(new VerificacaoNaResposta(Ok, Ok, Ok, Ok), await resposta.Content.ReadFromJsonAsync<VerificacaoNaResposta>(Cancelamento));
        Assert.Equal([HttpMethod.Post, HttpMethod.Patch, HttpMethod.Get, HttpMethod.Delete], ChamadasAAgenda(conta).Select(c => c.Metodo));
        Assert.All(ChamadasAAgenda(conta).Where(c => c.Metodo != HttpMethod.Get), chamada => Assert.Equal("none", SendUpdates(chamada)));
        Assert.Empty(banco.GoogleAgenda.EventosDe(conta));
    }

    [Fact]
    public async Task Verificacao_da_agenda_que_falha_ao_alterar_apaga_o_evento_criado_e_mostra_o_erro()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var conta = await ConectarAsync(logada);
        banco.GoogleAgenda.FalharAgenda(conta, HttpMethod.Patch, HttpStatusCode.InternalServerError);

        var verificacao = await (await logada.Navegador.PostarAsync($"{logada.Rota}/google/verificacao"))
            .Content.ReadFromJsonAsync<VerificacaoNaResposta>(Cancelamento);

        Assert.NotNull(verificacao);
        Assert.Equal(Ok, verificacao.Criado);
        Assert.False(verificacao.Alterado!.Ok);
        Assert.Contains("Falha de teste do Google falso", verificacao.Alterado.Erro);
        Assert.Null(verificacao.Listado);
        Assert.Equal(Ok, verificacao.Apagado);
        Assert.Empty(banco.GoogleAgenda.EventosDe(conta));
    }

    [Fact]
    public async Task Verificacao_da_agenda_sem_permissao_para_criar_para_no_primeiro_passo_e_mostra_o_erro()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var conta = await ConectarAsync(logada);
        banco.GoogleAgenda.FalharAgenda(conta, HttpMethod.Post, HttpStatusCode.Forbidden);

        var verificacao = await (await logada.Navegador.PostarAsync($"{logada.Rota}/google/verificacao"))
            .Content.ReadFromJsonAsync<VerificacaoNaResposta>(Cancelamento);

        Assert.NotNull(verificacao);
        Assert.False(verificacao.Criado.Ok);
        Assert.Contains("Falha de teste do Google falso", verificacao.Criado.Erro);
        Assert.Equal((null, null, null), (verificacao.Alterado, verificacao.Listado, verificacao.Apagado));
        Assert.Equal([HttpMethod.Post], ChamadasAAgenda(conta).Select(c => c.Metodo));
    }

    [Fact]
    public async Task Criar_evento_avisa_a_convidada_so_quando_pedido()
    {
        var conta = ContaDaAgenda.Nova();
        var profissional = await CadastrosTests.ProfissionalConectadaAsync(banco, conta);
        await using var escopo = banco.Api.Services.CreateAsyncScope();
        var agenda = escopo.ServiceProvider.GetRequiredService<AgendaGoogle>();

        await agenda.CriarEventoAsync(profissional.Id, new Event { Summary = "Com aviso" }, avisarConvidada: true, Cancelamento);
        await agenda.CriarEventoAsync(profissional.Id, new Event { Summary = "Sem aviso" }, avisarConvidada: false, Cancelamento);

        Assert.Equal(["all", "none"], ChamadasAAgenda(conta).Select(SendUpdates));
    }
}
