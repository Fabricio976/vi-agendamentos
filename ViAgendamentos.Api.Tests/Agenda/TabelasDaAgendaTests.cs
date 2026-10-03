using Npgsql;
using ViAgendamentos.Api.Agenda;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Agenda;

public class TabelasDaAgendaTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task<Guid> ProfissionalAsync()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        return (await CadastrosTests.ProfissionalAsync(db, salao.Id)).Id;
    }

    private static HorarioTrabalho Faixa(Guid profissionalId, DayOfWeek dia, int deHora, int ateHora) =>
        new() { ProfissionalId = profissionalId, DiaSemana = dia, Inicio = new(deHora, 0), Fim = new(ateHora, 0) };

    [Fact]
    public async Task Tabelas_servicos_e_horarios_trabalho_tem_colunas_em_snake_case()
    {
        Assert.Equal(
            new[] { "ativo", "duracao_minutos", "id", "nome", "profissional_id" },
            await banco.ColunasAsync("servicos"));
        Assert.Equal(
            new[] { "dia_semana", "fim", "id", "inicio", "profissional_id" },
            await banco.ColunasAsync("horarios_trabalho"));
    }

    [Fact]
    public async Task Servico_com_duracao_zero_e_recusado()
    {
        var profissionalId = await ProfissionalAsync();
        await using var escopo = banco.NovoEscopo(out var db);
        // Controle positivo: a menor duração que a API aceita passa no banco.
        db.Servicos.Add(new Servico { ProfissionalId = profissionalId, Nome = "Franja", DuracaoMinutos = 5 });
        await db.SaveChangesAsync(Cancelamento);

        db.Servicos.Add(new Servico { ProfissionalId = profissionalId, Nome = "Corte", DuracaoMinutos = 0 });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.CheckViolation,
            "ck_servicos_duracao_positiva");
    }

    [Fact]
    public async Task Servico_com_nome_em_branco_e_recusado()
    {
        var profissionalId = await ProfissionalAsync();
        await using var escopo = banco.NovoEscopo(out var db);
        db.Servicos.Add(new Servico { ProfissionalId = profissionalId, Nome = "   ", DuracaoMinutos = 30 });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.CheckViolation,
            "ck_servicos_nome_preenchido");
    }

    [Theory]
    [InlineData(12, 9)]
    [InlineData(12, 12)]
    public async Task Faixa_que_nao_termina_depois_de_comecar_e_recusada(int deHora, int ateHora)
    {
        var profissionalId = await ProfissionalAsync();
        await using var escopo = banco.NovoEscopo(out var db);
        // Controle positivo: a faixa das 9h às 12h passa.
        db.HorariosTrabalho.Add(Faixa(profissionalId, DayOfWeek.Monday, 9, 12));
        await db.SaveChangesAsync(Cancelamento);

        db.HorariosTrabalho.Add(Faixa(profissionalId, DayOfWeek.Tuesday, deHora, ateHora));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.CheckViolation,
            "ck_horarios_trabalho_fim_depois_do_inicio");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task Dia_da_semana_fora_de_0_a_6_e_recusado(int dia)
    {
        var profissionalId = await ProfissionalAsync();
        await using var escopo = banco.NovoEscopo(out var db);
        db.HorariosTrabalho.Add(Faixa(profissionalId, (DayOfWeek)dia, 9, 12));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.CheckViolation,
            "ck_horarios_trabalho_dia_semana");
    }

    [Fact]
    public async Task Servico_exige_profissional_que_existe()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        db.Servicos.Add(new Servico { ProfissionalId = Guid.NewGuid(), Nome = "Corte", DuracaoMinutos = 30 });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_servicos_profissional");
    }

    [Fact]
    public async Task Faixa_exige_profissional_que_existe()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        db.HorariosTrabalho.Add(Faixa(Guid.NewGuid(), DayOfWeek.Monday, 9, 12));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(Cancelamento),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_horarios_trabalho_profissional");
    }
}
