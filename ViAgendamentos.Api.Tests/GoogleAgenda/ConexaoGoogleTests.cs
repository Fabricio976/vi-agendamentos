using Npgsql;
using ViAgendamentos.Api.GoogleAgenda;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.GoogleAgenda;

public class ConexaoGoogleTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static ConexaoGoogle NovaConexao(Guid profissionalId)
    {
        var conexao = new ConexaoGoogle { ProfissionalId = profissionalId };
        conexao.Conectar(CadastrosTests.EmailUnico(), [1, 2, 3], DateTimeOffset.UtcNow);
        return conexao;
    }

    [Fact]
    public async Task Tabela_conexoes_google_tem_colunas_em_snake_case()
    {
        var colunas = await banco.ColunasAsync("conexoes_google");

        Assert.Equal(
            new[] { "conectado_em", "google_email", "id", "profissional_id", "refresh_token_cifrado", "status", "ultimo_erro" },
            colunas);
    }

    // Em escopos separados: o EF não sabe da primeira conexão, e quem recusa a segunda é o banco.
    [Fact]
    public async Task Profissional_tem_uma_conexao_so()
    {
        Guid profissionalId;
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            var salao = await CadastrosTests.SalaoAsync(db);
            profissionalId = (await CadastrosTests.ProfissionalAsync(db, salao.Id)).Id;
            db.ConexoesGoogle.Add(NovaConexao(profissionalId));
            await db.SaveChangesAsync(Cancelamento);
        }

        await using var outroEscopo = banco.NovoEscopo(out var outroDb);
        outroDb.ConexoesGoogle.Add(NovaConexao(profissionalId));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => outroDb.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.UniqueViolation,
            "uq_conexoes_google_profissional");
    }

    [Fact]
    public async Task Conexao_exige_profissional_que_existe()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        db.ConexoesGoogle.Add(NovaConexao(Guid.NewGuid()));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_conexoes_google_profissional");
    }
}
