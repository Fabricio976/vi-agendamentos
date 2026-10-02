using System.Net.Http.Json;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Saloes;

// A resposta de /api/saloes como o front lê: o papel chega como texto.
internal sealed record SalaoNaResposta(Guid Id, string Nome, string Slug, string Role);

public class SaloesDaProfissionalTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task<(Salao Salao, string Email)> SalaoComProfissionalAsync(Role role = Role.Dona, string? email = null)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        email ??= CadastrosTests.EmailUnico();
        db.Profissionais.Add(new Profissional { SalaoId = salao.Id, Nome = "Vi", Email = email, Role = role });
        await db.SaveChangesAsync(Cancelamento);
        return (salao, email);
    }

    [Fact]
    public async Task Login_liga_a_profissional_convidada_com_o_mesmo_email_mesmo_com_maiusculas()
    {
        var (salao, email) = await SalaoComProfissionalAsync();
        using var navegador = banco.NovoNavegador();

        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(email.ToUpperInvariant()));

        var saloes = await navegador.Http.GetFromJsonAsync<List<SalaoNaResposta>>("/api/saloes", Cancelamento);
        Assert.Equal(new SalaoNaResposta(salao.Id, salao.Nome, salao.Slug, "dona"), Assert.Single(saloes!));
    }

    [Fact]
    public async Task Saloes_saem_em_ordem_de_nome()
    {
        var email = CadastrosTests.EmailUnico();
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            // Gravados um a um, ao contrário da ordem esperada: sem ordenar, o banco devolve na ordem em que gravou.
            foreach (var nome in new[] { "Zeta Studio", "Alfa Salão" })
            {
                var salao = new Salao { Nome = nome, Slug = CadastrosTests.SlugUnico() };
                db.Saloes.Add(salao);
                db.Profissionais.Add(new Profissional { SalaoId = salao.Id, Nome = "Vi", Email = email, Role = Role.Funcionaria });
                await db.SaveChangesAsync(Cancelamento);
            }
        }
        using var navegador = banco.NovoNavegador();

        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(email));

        var saloes = await navegador.Http.GetFromJsonAsync<List<SalaoNaResposta>>("/api/saloes", Cancelamento);
        Assert.Equal(["Alfa Salão", "Zeta Studio"], saloes!.Select(salao => salao.Nome));
    }

    [Fact]
    public async Task Profissional_do_salao_a_nao_ve_o_salao_b()
    {
        var (salaoA, emailA) = await SalaoComProfissionalAsync();
        var (_, emailB) = await SalaoComProfissionalAsync();
        using (var donaB = banco.NovoNavegador())
        {
            await donaB.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(emailB));
        }
        using var donaA = banco.NovoNavegador();

        await donaA.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(emailA));

        var saloes = await donaA.Http.GetFromJsonAsync<List<SalaoNaResposta>>("/api/saloes", Cancelamento);
        Assert.Equal(salaoA.Id, Assert.Single(saloes!).Id);
    }

    [Fact]
    public async Task Convite_feito_depois_do_primeiro_login_liga_no_login_seguinte()
    {
        var conta = ContaGoogle.Nova();
        using (var primeiraVez = banco.NovoNavegador())
        {
            await primeiraVez.EntrarComGoogleAsync(banco.Google, conta);
        }
        var (salao, _) = await SalaoComProfissionalAsync(Role.Funcionaria, conta.Email);
        using var navegador = banco.NovoNavegador();

        await navegador.EntrarComGoogleAsync(banco.Google, conta);

        var saloes = await navegador.Http.GetFromJsonAsync<List<SalaoNaResposta>>("/api/saloes", Cancelamento);
        Assert.Equal(new SalaoNaResposta(salao.Id, salao.Nome, salao.Slug, "funcionaria"), Assert.Single(saloes!));
    }
}
