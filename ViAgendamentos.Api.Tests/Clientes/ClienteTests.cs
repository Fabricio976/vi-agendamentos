using Npgsql;
using ViAgendamentos.Api.Clientes;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Tests.Clientes;

public class ClienteTests(PostgresFixtureTests banco)
{
    private const string PhoneValido = "5511987654321";

    [Fact]
    public async Task Tabela_clientes_tem_colunas_em_snake_case()
    {
        var colunas = await banco.ColunasAsync("clientes");

        Assert.Equal(new[] { "criada_por", "email", "id", "nome", "phone", "salao_id", "sobrenome", "usuario_id" }, colunas);
    }

    // Controle positivo das checagens do link e do telefone.
    [Fact]
    public async Task Cliente_da_profissional_so_com_nome_e_cliente_do_link_completa_sao_aceitas()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Clientes.AddRange(
            new Cliente { SalaoId = salao.Id, Nome = "Dona Rosa", CriadaPor = Origem.Profissional },
            new Cliente { SalaoId = salao.Id, Nome = "Ana", CriadaPor = Origem.Link, Email = CadastrosTests.EmailUnico(), Phone = PhoneValido });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Cliente_do_link_sem_email_ou_sem_phone_e_recusada(bool temEmail, bool temPhone)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Clientes.Add(new Cliente
        {
            SalaoId = salao.Id,
            Nome = "Ana",
            CriadaPor = Origem.Link,
            Email = temEmail ? CadastrosTests.EmailUnico() : null,
            Phone = temPhone ? PhoneValido : null,
        });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_clientes_link_exige_email_e_phone");
    }

    [Fact]
    public async Task Phone_fora_do_formato_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Clientes.Add(new Cliente { SalaoId = salao.Id, Nome = "Ana", CriadaPor = Origem.Profissional, Phone = "11987654321" });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_clientes_phone");
    }

    [Fact]
    public async Task Mesmo_email_no_mesmo_salao_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var outroSalao = await CadastrosTests.SalaoAsync(db);
        var email = CadastrosTests.EmailUnico();

        // Controles positivos: o mesmo e-mail em outro salão, e duas clientes sem e-mail no mesmo salão.
        db.Clientes.AddRange(
            new Cliente { SalaoId = salao.Id, Nome = "Ana", Email = email, CriadaPor = Origem.Profissional },
            new Cliente { SalaoId = outroSalao.Id, Nome = "Ana", Email = email, CriadaPor = Origem.Profissional },
            new Cliente { SalaoId = salao.Id, Nome = "Dona Rosa", CriadaPor = Origem.Profissional },
            new Cliente { SalaoId = salao.Id, Nome = "Dona Lurdes", CriadaPor = Origem.Profissional });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Clientes.Add(new Cliente { SalaoId = salao.Id, Nome = "Outra Ana", Email = email, CriadaPor = Origem.Profissional });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "uq_clientes_salao_email");
    }

    [Fact]
    public async Task Email_com_maiuscula_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Clientes.Add(new Cliente { SalaoId = salao.Id, Nome = "Ana", Email = "Ana@Teste.com", CriadaPor = Origem.Profissional });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_clientes_email_minusculo");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ana@teste.com")]
    [InlineData("ana@teste.com ")]
    public async Task Email_vazio_ou_com_espaco_nas_pontas_e_recusado(string email)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Clientes.Add(new Cliente { SalaoId = salao.Id, Nome = "Ana", Email = email, CriadaPor = Origem.Profissional });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_clientes_email_preenchido");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Nome_vazio_e_recusado(string nome)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        db.Clientes.Add(new Cliente { SalaoId = salao.Id, Nome = nome, CriadaPor = Origem.Profissional });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_clientes_nome_preenchido");
    }

    [Fact]
    public async Task Salao_com_cliente_nao_e_apagado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var comCliente = await CadastrosTests.SalaoAsync(db);
        db.Clientes.Add(new Cliente { SalaoId = comCliente.Id, Nome = "Dona Rosa", CriadaPor = Origem.Profissional });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => CadastrosTests.ApagarAsync<Salao>(banco, comCliente.Id),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_clientes_salao");
    }

    [Fact]
    public async Task Usuario_ligado_a_uma_cliente_nao_e_apagado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var usuario = await CadastrosTests.UsuarioAsync(db);
        db.Clientes.Add(new Cliente { SalaoId = salao.Id, Nome = "Dona Rosa", CriadaPor = Origem.Profissional, UsuarioId = usuario.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => CadastrosTests.ApagarAsync<Usuario>(banco, usuario.Id),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_clientes_usuario");
    }

    [Fact]
    public async Task Mesmo_usuario_duas_vezes_no_mesmo_salao_e_recusado()
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var salao = await CadastrosTests.SalaoAsync(db);
        var outroSalao = await CadastrosTests.SalaoAsync(db);
        var usuario = await CadastrosTests.UsuarioAsync(db);

        // Controle positivo: o mesmo usuário em outro salão é permitido.
        db.Clientes.AddRange(
            new Cliente { SalaoId = salao.Id, Nome = "Ana", CriadaPor = Origem.Profissional, UsuarioId = usuario.Id },
            new Cliente { SalaoId = outroSalao.Id, Nome = "Ana", CriadaPor = Origem.Profissional, UsuarioId = usuario.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Clientes.Add(new Cliente { SalaoId = salao.Id, Nome = "Ana de novo", CriadaPor = Origem.Profissional, UsuarioId = usuario.Id });

        await ErroDoBancoTests.RestricaoVioladaAsync(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "uq_clientes_salao_usuario");
    }
}
