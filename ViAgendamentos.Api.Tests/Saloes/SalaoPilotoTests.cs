using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Saloes;

public class SalaoPilotoTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    // A API com os dados do comando, como chegam pela linha de comando.
    private WebApplicationFactory<Program> ComandoCom(string slug, string donaEmail) => banco.ApiCom(
        ("SalaoPiloto:Nome", "Salão da Vi"),
        ("SalaoPiloto:Slug", slug),
        ("SalaoPiloto:DonaNome", "Vi"),
        ("SalaoPiloto:DonaEmail", donaEmail));

    [Fact]
    public async Task Comando_cadastra_o_salao_e_a_dona_com_o_email_em_minusculas()
    {
        var slug = CadastrosTests.SlugUnico();
        var email = CadastrosTests.EmailUnico();
        using var api = ComandoCom(slug, email.ToUpperInvariant());

        await SalaoPiloto.CadastrarAsync(api.Services, Cancelamento);

        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await db.Saloes.SingleAsync(s => s.Slug == slug, Cancelamento);
        var dona = await db.Profissionais.SingleAsync(p => p.SalaoId == salao.Id, Cancelamento);
        Assert.Equal("Salão da Vi", salao.Nome);
        Assert.Equal(("Vi", email, Role.Dona), (dona.Nome, dona.Email, dona.Role));
    }

    [Fact]
    public async Task Comando_liga_na_hora_a_dona_que_ja_tem_sessao_aberta()
    {
        var conta = ContaGoogle.Nova();
        using var navegador = banco.NovoNavegador();
        await navegador.EntrarComGoogleAsync(banco.Google, conta);
        var slug = CadastrosTests.SlugUnico();
        using var api = ComandoCom(slug, conta.Email.ToUpperInvariant());

        await SalaoPiloto.CadastrarAsync(api.Services, Cancelamento);

        var saloes = await navegador.Http.GetFromJsonAsync<List<SalaoNaResposta>>("/api/saloes", Cancelamento);
        var salao = Assert.Single(saloes!);
        Assert.Equal((slug, "dona"), (salao.Slug, salao.Role));
    }

    [Fact]
    public async Task Sem_os_dados_o_comando_falha_com_a_mensagem_dos_campos()
    {
        var erro = await Assert.ThrowsAsync<OptionsValidationException>(
            () => SalaoPiloto.CadastrarAsync(banco.Api.Services, Cancelamento));

        Assert.Contains("DonaEmail", erro.Message);
    }

    [Fact]
    public async Task Email_da_dona_fora_do_formato_faz_o_comando_falhar_sem_cadastrar()
    {
        var slug = CadastrosTests.SlugUnico();
        using var api = ComandoCom(slug, "dona.gmail.com");

        var erro = await Assert.ThrowsAsync<OptionsValidationException>(
            () => SalaoPiloto.CadastrarAsync(api.Services, Cancelamento));

        Assert.Contains("DonaEmail", erro.Message);
        await using var escopo = banco.NovoEscopo(out var db);
        Assert.False(await db.Saloes.AnyAsync(s => s.Slug == slug, Cancelamento));
    }
}
