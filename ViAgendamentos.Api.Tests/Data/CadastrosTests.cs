using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Tests.Data;

// Dados únicos por teste: a suíte roda em paralelo sobre o mesmo banco.
public static class CadastrosTests
{
    public static string SlugUnico() => $"salao-{Guid.NewGuid():N}";

    public static string EmailUnico() => $"{Guid.NewGuid():N}@teste.com";

    public static async Task<Salao> SalaoAsync(AppDbContext db)
    {
        var salao = new Salao { Nome = "Salão de Teste", Slug = SlugUnico() };
        db.Saloes.Add(salao);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return salao;
    }

    public static async Task<Usuario> UsuarioAsync(AppDbContext db)
    {
        var usuario = new Usuario { Nome = "Pessoa de Teste" };
        db.Users.Add(usuario);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return usuario;
    }

    public static async Task<Profissional> ProfissionalAsync(AppDbContext db, Guid salaoId, Role role = Role.Dona, string? email = null)
    {
        var profissional = new Profissional { SalaoId = salaoId, Nome = "Vi", Email = email ?? EmailUnico(), Role = role };
        db.Profissionais.Add(profissional);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return profissional;
    }

    // Dona de um salão novo, com a sessão aberta pelo login com Google, que liga a profissional pelo e-mail.
    public static async Task<ProfissionalLogada> ProfissionalLogadaAsync(PostgresFixtureTests banco)
    {
        Salao salao;
        Profissional profissional;
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            salao = await SalaoAsync(db);
            profissional = await ProfissionalAsync(db, salao.Id);
        }

        var navegador = banco.NovoNavegador();
        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova(profissional.Email));
        return new ProfissionalLogada(navegador, salao, profissional);
    }

    // Apaga num escopo novo, sem os dependentes carregados: quem decide o que acontece com eles é o banco.
    public static async Task ApagarAsync<TEntidade>(PostgresFixtureTests banco, Guid id) where TEntidade : class
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var entidade = await db.Set<TEntidade>().FindAsync([id], TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"Nada para apagar: {typeof(TEntidade).Name} {id} não existe.");
        db.Remove(entidade);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}

public sealed record ProfissionalLogada(NavegadorTests Navegador, Salao Salao, Profissional Profissional) : IDisposable
{
    public string Rota => $"/api/saloes/{Salao.Id}/profissionais/{Profissional.Id}";

    public void Dispose() => Navegador.Dispose();
}
