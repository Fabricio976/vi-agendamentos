using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ViAgendamentos.Api.Tests.Data;

public static class ErroDoBancoTests
{
    // Confere o código e o nome da restrição: um erro qualquer do banco não prova a regra.
    public static async Task RestricaoVioladaAsync(Func<Task> gravar, string sqlState, string restricao)
    {
        var postgres = await ErroDoPostgresAsync(gravar);
        Assert.Equal(sqlState, postgres.SqlState);
        Assert.Equal(restricao, postgres.ConstraintName);
    }

    // NOT NULL não tem nome de restrição no Postgres: o erro traz a coluna.
    public static async Task ColunaObrigatoriaAsync(Func<Task> gravar, string coluna)
    {
        var postgres = await ErroDoPostgresAsync(gravar);
        Assert.Equal(PostgresErrorCodes.NotNullViolation, postgres.SqlState);
        Assert.Equal(coluna, postgres.ColumnName);
    }

    private static async Task<PostgresException> ErroDoPostgresAsync(Func<Task> gravar)
    {
        var erro = await Assert.ThrowsAsync<DbUpdateException>(gravar);
        return Assert.IsType<PostgresException>(erro.InnerException);
    }
}
