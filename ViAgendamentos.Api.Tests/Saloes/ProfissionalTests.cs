using Microsoft.EntityFrameworkCore;
using Npgsql;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Tests.Saloes;

public class ProfissionalTests(PostgresFixtureTests banco)
{
    private static Profissional NovaProfissional(Guid salaoId, string? email = null, Guid? usuarioId = null) => new()
    {
        SalaoId = salaoId,
        Nome = "Vi",
        Email = email ?? CadastrosTests.EmailUnico(),
        Role = Role.Dona,
        UsuarioId = usuarioId,
    };

    [Fact]
    public async Task Tabela_profissionais_tem_colunas_em_snake_case()
    {
        var colunas = await banco.ColunasAsync("profissionais");

        Assert.Equal(
            new[] { "antecedencia_min_minutos", "ativo", "aviso_minutos", "email", "id", "janela_dias", "nome", "passo_minutos", "phone", "role", "salao_id", "sobrenome", "usuario_id" },
            colunas);
    }

    [Fact]
    public async Task Padroes_da_profissional_valem_e_zero_e_gravado_como_zero()
    {
        await using var escrita = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var padrao = NovaProfissional(salao.Id);
        var zerada = NovaProfissional(salao.Id);
        zerada.AvisoMinutos = 0;
        zerada.AntecedenciaMinMinutos = 0;
        db.Profissionais.AddRange(padrao, zerada);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Relê em outro escopo, para comparar o que o banco guardou, e não o objeto em memória.
        await using var leitura = banco.NovoEscopo(out var dbLeitura);
        var gravadas = await dbLeitura.Profissionais
            .Where(p => p.Id == padrao.Id || p.Id == zerada.Id)
            .ToDictionaryAsync(p => p.Id, TestContext.Current.CancellationToken);

        Assert.True(gravadas[padrao.Id].Ativo);
        Assert.Equal(15, gravadas[padrao.Id].AvisoMinutos);
        Assert.Equal(120, gravadas[padrao.Id].AntecedenciaMinMinutos);
        Assert.Equal(30, gravadas[padrao.Id].JanelaDias);
        Assert.Equal(15, gravadas[padrao.Id].PassoMinutos);
        Assert.Equal(0, gravadas[zerada.Id].AvisoMinutos);
        Assert.Equal(0, gravadas[zerada.Id].AntecedenciaMinMinutos);
    }

    [Fact]
    public async Task Mesmo_email_no_mesmo_salao_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var outroSalao = await CadastrosTests.SalaoAsync(db);
        var email = CadastrosTests.EmailUnico();

        // Controle positivo: o mesmo e-mail em outro salão é permitido.
        db.Profissionais.AddRange(NovaProfissional(salao.Id, email), NovaProfissional(outroSalao.Id, email));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Profissionais.Add(NovaProfissional(salao.Id, email));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "uq_profissionais_salao_email");
    }

    [Fact]
    public async Task Mesmo_usuario_duas_vezes_no_mesmo_salao_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var outroSalao = await CadastrosTests.SalaoAsync(db);
        var usuario = await CadastrosTests.UsuarioAsync(db);

        // Controle positivo: o mesmo usuário em outro salão é permitido.
        db.Profissionais.AddRange(
            NovaProfissional(salao.Id, usuarioId: usuario.Id),
            NovaProfissional(outroSalao.Id, usuarioId: usuario.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Profissionais.Add(NovaProfissional(salao.Id, usuarioId: usuario.Id));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "uq_profissionais_salao_usuario");
    }

    [Fact]
    public async Task Email_com_maiuscula_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Profissionais.Add(NovaProfissional(salao.Id, "Maria@Teste.com"));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_profissionais_email_minusculo");
    }

    [Fact]
    public async Task Profissional_sem_email_e_recusada()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var semEmail = NovaProfissional(salao.Id);
        semEmail.Email = null;
        db.Profissionais.Add(semEmail);

        await ErroDoBancoTests.ColunaObrigatoriaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            "email");
    }

    [Fact]
    public async Task Email_vazio_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Profissionais.Add(NovaProfissional(salao.Id, ""));

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_profissionais_email_preenchido");
    }

    [Fact]
    public async Task Nome_vazio_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var profissional = NovaProfissional(salao.Id);
        profissional.Nome = "  ";
        db.Profissionais.Add(profissional);

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_profissionais_nome_preenchido");
    }

    // Um caso só: os detalhes da regex compartilhada ficam em SalaoTests.
    [Fact]
    public async Task Phone_fora_do_formato_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var profissional = NovaProfissional(salao.Id);
        profissional.Phone = "11987654321";
        db.Profissionais.Add(profissional);

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_profissionais_phone");
    }

    // Controle positivo da checagem de telefone da profissional.
    [Fact]
    public async Task Phone_no_formato_e_aceito()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var profissional = NovaProfissional(salao.Id);
        profissional.Phone = "5511987654321";
        db.Profissionais.Add(profissional);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Salao_com_profissional_nao_e_apagado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var comProfissional = await CadastrosTests.SalaoAsync(db);
        db.Profissionais.Add(NovaProfissional(comProfissional.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => CadastrosTests.ApagarAsync<Salao>(banco, comProfissional.Id),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_profissionais_salao");
    }

    [Fact]
    public async Task Usuario_ligado_a_uma_profissional_nao_e_apagado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var usuario = await CadastrosTests.UsuarioAsync(db);
        db.Profissionais.Add(NovaProfissional(salao.Id, usuarioId: usuario.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => CadastrosTests.ApagarAsync<Usuario>(banco, usuario.Id),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_profissionais_usuario");
    }
}
