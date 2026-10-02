using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Saloes;

// A resposta de /api/saloes/{id} como o front lê: o papel chega como texto.
internal sealed record SalaoNaSessao(Guid Id, string Nome, string Slug, string Fuso, Guid ProfissionalId, string Role);

public class AuthRequirementTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Profissional_le_o_proprio_salao()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var salao = await logada.Navegador.Http.GetFromJsonAsync<SalaoNaSessao>($"/api/saloes/{logada.Salao.Id}", Cancelamento);

        Assert.Equal(
            new SalaoNaSessao(logada.Salao.Id, logada.Salao.Nome, logada.Salao.Slug, "America/Sao_Paulo", logada.Profissional.Id, "dona"),
            salao);
    }

    [Fact]
    public async Task Profissional_de_outro_salao_recebe_403()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        Salao outro;
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            outro = await CadastrosTests.SalaoAsync(db);
            await CadastrosTests.ProfissionalAsync(db, outro.Id);
        }

        var resposta = await logada.Navegador.Http.GetAsync($"/api/saloes/{outro.Id}", Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Profissional_inativa_recebe_403()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var rota = $"/api/saloes/{logada.Salao.Id}";
        Assert.Equal(HttpStatusCode.OK, (await logada.Navegador.Http.GetAsync(rota, Cancelamento)).StatusCode);
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            await db.Profissionais
                .Where(p => p.Id == logada.Profissional.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Ativo, false), Cancelamento);
        }

        var resposta = await logada.Navegador.Http.GetAsync(rota, Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_sessao_a_rota_do_salao_responde_401()
    {
        Salao salao;
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            salao = await CadastrosTests.SalaoAsync(db);
        }
        using var navegador = banco.NovoNavegador();

        var resposta = await navegador.Http.GetAsync($"/api/saloes/{salao.Id}", Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Salao_inexistente_recebe_403_e_nao_500()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var resposta = await logada.Navegador.Http.GetAsync($"/api/saloes/{Guid.NewGuid()}", Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }
}
