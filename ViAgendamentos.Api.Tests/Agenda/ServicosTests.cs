using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Agenda;

internal sealed record ServicoNaResposta(Guid Id, string Nome, int DuracaoMinutos, bool Ativo);

public class ServicosTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static async Task<List<ServicoNaResposta>> ListarAsync(ProfissionalLogada logada) =>
        (await logada.Navegador.Http.GetFromJsonAsync<List<ServicoNaResposta>>($"{logada.Rota}/servicos", Cancelamento))!;

    private static Task<HttpResponseMessage> CriarAsync(ProfissionalLogada logada, object servico) =>
        logada.Navegador.PostarAsync($"{logada.Rota}/servicos", servico);

    // Uma funcionária do mesmo salão, com um serviço dela.
    private async Task<(Guid ProfissionalId, Guid ServicoId)> OutraProfissionalAsync(ProfissionalLogada logada)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var outra = await CadastrosTests.ProfissionalAsync(db, logada.Salao.Id, Role.Funcionaria);
        return (outra.Id, (await CadastrosTests.ServicoAsync(db, outra.Id)).Id);
    }

    // Criados fora da ordem de nome: a lista sai em ordem mesmo assim.
    [Fact]
    public async Task Profissional_cria_e_lista_os_proprios_servicos()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var escova = await CriarAsync(logada, new { nome = "Escova", duracaoMinutos = 45 });
        await CriarAsync(logada, new { nome = "Corte", duracaoMinutos = 30 });

        Assert.Equal(HttpStatusCode.Created, escova.StatusCode);
        Assert.Equal(
            [("Corte", 30, true), ("Escova", 45, true)],
            (await ListarAsync(logada)).Select(s => (s.Nome, s.DuracaoMinutos, s.Ativo)));
    }

    [Fact]
    public async Task Profissional_altera_e_desativa_o_proprio_servico()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var criado = await (await CriarAsync(logada, new { nome = "Corte", duracaoMinutos = 30 }))
            .Content.ReadFromJsonAsync<ServicoNaResposta>(Cancelamento);

        var resposta = await logada.Navegador.EnviarAsync(
            HttpMethod.Put, $"{logada.Rota}/servicos/{criado!.Id}", new { nome = "Corte longo", duracaoMinutos = 50, ativo = false });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal([new ServicoNaResposta(criado.Id, "Corte longo", 50, false)], await ListarAsync(logada));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(481)]
    public async Task Servico_com_duracao_fora_da_faixa_volta_com_erro_no_campo(int duracao)
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var resposta = await CriarAsync(logada, new { nome = "Corte", duracaoMinutos = duracao });

        Assert.Equal(["A duração vai de 5 a 480 minutos."], await ErroDeValidacaoTests.NoCampoAsync(resposta, "DuracaoMinutos"));
        Assert.Empty(await ListarAsync(logada));
    }

    [Fact]
    public async Task Servico_com_nome_em_branco_volta_com_erro_no_campo()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var resposta = await CriarAsync(logada, new { nome = "   ", duracaoMinutos = 30 });

        Assert.Equal(["Informe o nome do serviço."], await ErroDeValidacaoTests.NoCampoAsync(resposta, "Nome"));
        Assert.Empty(await ListarAsync(logada));
    }

    [Fact]
    public async Task Alterar_servico_de_outra_profissional_responde_404()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var (_, daOutra) = await OutraProfissionalAsync(logada);

        var resposta = await logada.Navegador.EnviarAsync(
            HttpMethod.Put, $"{logada.Rota}/servicos/{daOutra}", new { nome = "Trocado", duracaoMinutos = 30, ativo = true });

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        await using var escopo = banco.NovoEscopo(out var db);
        Assert.Equal("Corte", (await db.Servicos.SingleAsync(s => s.Id == daOutra, Cancelamento)).Nome);
    }

    [Fact]
    public async Task Lista_nao_traz_servico_de_outra_profissional()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        await OutraProfissionalAsync(logada);
        await CriarAsync(logada, new { nome = "Escova", duracaoMinutos = 45 });

        Assert.Equal(["Escova"], (await ListarAsync(logada)).Select(s => s.Nome));
    }

    // Por enquanto, nem a dona mexe na agenda das outras: o grupo exige a própria profissional.
    [Fact]
    public async Task Rota_de_outra_profissional_do_salao_responde_403()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var (outraId, _) = await OutraProfissionalAsync(logada);

        var resposta = await logada.Navegador.Http.GetAsync($"/api/saloes/{logada.Salao.Id}/profissionais/{outraId}/servicos", Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Criar_servico_sem_cabecalho_antifalsificacao_e_recusado()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var resposta = await logada.Navegador.Http.PostAsJsonAsync(
            $"{logada.Rota}/servicos", new { nome = "Corte", duracaoMinutos = 30 }, Cancelamento);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Empty(await ListarAsync(logada));
    }
}
