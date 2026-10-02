using Npgsql;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Saloes;

public class SalaoTests(PostgresFixtureTests banco)
{
    [Fact]
    public async Task Tabela_saloes_tem_colunas_em_snake_case()
    {
        var colunas = await banco.ColunasAsync("saloes");

        Assert.Equal(new[] { "fuso", "id", "nome", "phone", "slug" }, colunas);
    }

    [Fact]
    public async Task Slug_repetido_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var primeiro = await CadastrosTests.SalaoAsync(db);

        db.Saloes.Add(new Salao { Nome = "Outro salão", Slug = primeiro.Slug });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "uq_saloes_slug");
    }

    [Theory]
    [InlineData("11987654321")]
    [InlineData("55 11 98765-4321")]
    [InlineData("5501987654321")]
    [InlineData("55119876543210")]
    public async Task Phone_fora_do_formato_e_recusado(string phone)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        db.Saloes.Add(new Salao { Nome = "Salão", Slug = CadastrosTests.SlugUnico(), Phone = phone });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_saloes_phone");
    }

    // Controle positivo da checagem: celular e fixo com 55 e DDD passam, e salão sem telefone também.
    [Theory]
    [InlineData("5511987654321")]
    [InlineData("551133334444")]
    [InlineData(null)]
    public async Task Phone_no_formato_ou_vazio_e_aceito(string? phone)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        db.Saloes.Add(new Salao { Nome = "Salão", Slug = CadastrosTests.SlugUnico(), Phone = phone });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
