using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Agenda;
using ViAgendamentos.Api.Clientes;
using ViAgendamentos.Api.GoogleAgenda;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Data;

// IdentityUserContext, e não IdentityDbContext: o papel mora em profissionais.role, e as tabelas de papéis do Identity ficariam sem uso.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<Usuario, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Salao> Saloes => Set<Salao>();
    public DbSet<Profissional> Profissionais => Set<Profissional>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ConexaoGoogle> ConexoesGoogle => Set<ConexaoGoogle>();
    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<HorarioTrabalho> HorariosTrabalho => Set<HorarioTrabalho>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // O Identity fixa os nomes das tabelas e dos índices dele, e a convenção de snake_case não mexe em nome fixado.
        modelBuilder.Entity<Usuario>(usuario =>
        {
            usuario.ToTable("usuarios");
            usuario.HasIndex(u => u.NormalizedUserName).HasDatabaseName("uq_usuarios_normalized_user_name");
            usuario.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_usuarios_normalized_email");
        });
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("usuario_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("usuario_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("usuario_tokens");
        modelBuilder.Entity<DataProtectionKey>().ToTable("chaves_protecao");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
