using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Usuarios;

public class UsuarioTests(PostgresFixtureTests banco)
{
    [Fact]
    public async Task Tabela_usuarios_tem_colunas_em_snake_case()
    {
        var colunas = await banco.ColunasAsync("usuarios");

        Assert.Equal(
            new[]
            {
                "access_failed_count", "concurrency_stamp", "email", "email_confirmed", "id", "lockout_enabled",
                "lockout_end", "nome", "normalized_email", "normalized_user_name", "password_hash", "phone_number",
                "phone_number_confirmed", "security_stamp", "two_factor_enabled", "user_name",
            },
            colunas);
    }
}
