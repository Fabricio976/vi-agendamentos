using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Saloes;

namespace ViAgendamentos.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Salao> Saloes => Set<Salao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
