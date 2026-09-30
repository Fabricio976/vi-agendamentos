using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Saloes;

namespace ViAgendamentos.Api.Tests.Data;

// Dados únicos por teste: a suíte roda em paralelo sobre o mesmo banco.
public static class Cadastros
{
    public static string SlugUnico() => $"salao-{Guid.NewGuid():N}";

    public static async Task<Salao> SalaoAsync(AppDbContext db)
    {
        var salao = new Salao { Nome = "Salão de Teste", Slug = SlugUnico() };
        db.Saloes.Add(salao);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return salao;
    }
}
