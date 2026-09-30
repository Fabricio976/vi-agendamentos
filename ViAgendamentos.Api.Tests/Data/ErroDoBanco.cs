using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ViAgendamentos.Api.Tests.Data;

public static class ErroDoBanco
{
    // Confere o código e o nome da restrição: um erro qualquer do banco não prova a regra.
    public static async Task RestricaoVioladaAsync(Func<Task> gravar, string sqlState, string restricao)
    {
        var erro = await Assert.ThrowsAsync<DbUpdateException>(gravar);
        var postgres = Assert.IsType<PostgresException>(erro.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        Assert.Equal(restricao, postgres.ConstraintName);
    }
}
