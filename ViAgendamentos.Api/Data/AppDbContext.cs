using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Clientes;
using ViAgendamentos.Api.Saloes;

namespace ViAgendamentos.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Salao> Saloes => Set<Salao>();
    public DbSet<Profissional> Profissionais => Set<Profissional>();
    public DbSet<Cliente> Clientes => Set<Cliente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
