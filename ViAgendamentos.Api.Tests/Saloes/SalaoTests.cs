using Npgsql;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Saloes;

public class SalaoTests(PostgresFixture banco)
{
    // Pega: migration gerada sem o snake_case, com colunas como "Whatsapp", fora da spec.
    [Fact]
    public async Task Tabela_saloes_tem_as_colunas_da_spec()
    {
        var colunas = await banco.ColunasAsync("saloes");

        Assert.Equal(new[] { "fuso", "id", "nome", "slug", "whatsapp" }, colunas);
    }

    // Pega: slug sem índice único, que daria o mesmo link público a dois salões.
    [Fact]
    public async Task Slug_repetido_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var primeiro = await Cadastros.SalaoAsync(db);

        db.Saloes.Add(new Salao { Nome = "Outro salão", Slug = primeiro.Slug });

        await ErroDoBanco.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "uq_saloes_slug");
    }

    // Pega: WhatsApp do salão sem checagem de formato, que quebraria o botão de falar com o salão.
    [Theory]
    [InlineData("11987654321")]
    [InlineData("55 11 98765-4321")]
    [InlineData("5501987654321")]
    public async Task Whatsapp_fora_do_formato_e_recusado(string whatsapp)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        db.Saloes.Add(new Salao { Nome = "Salão", Slug = Cadastros.SlugUnico(), Whatsapp = whatsapp });

        await ErroDoBanco.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_saloes_whatsapp");
    }

    // Controle positivo da checagem: celular e fixo com 55 e DDD passam, e salão sem WhatsApp também.
    [Theory]
    [InlineData("5511987654321")]
    [InlineData("551133334444")]
    [InlineData(null)]
    public async Task Whatsapp_no_formato_ou_vazio_e_aceito(string? whatsapp)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        db.Saloes.Add(new Salao { Nome = "Salão", Slug = Cadastros.SlugUnico(), Whatsapp = whatsapp });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
