using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Saloes;

namespace ViAgendamentos.Api.Pessoas;

// O EF não mapeia Pessoa: cada filha tem a própria tabela.
public abstract class Pessoa
{
    public Guid Id { get; set; }
    public Guid SalaoId { get; set; }
    public required string Nome { get; set; }
    public string? Sobrenome { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

// Cada tabela filha ganha a própria cópia das regras no banco, com o nome dela nas restrições.
internal abstract class PessoaConfiguration<T>(string tabela) : IEntityTypeConfiguration<T> where T : Pessoa
{
    public virtual void Configure(EntityTypeBuilder<T> pessoa)
    {
        //@ManyToOne + @JoinColumn(name = "salao_id"). ex: JAVA
        pessoa.HasOne<Salao>()
            .WithMany()
            .HasForeignKey(p => p.SalaoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName($"fk_{tabela}_salao");
        pessoa.HasIndex(p => new { p.SalaoId, p.Email })//é um tipo anônimo, usado para indicar um índice composto (duas colunas). Java: @Table(uniqueConstraints = @UniqueConstraint(columnNames = {"salao_id", "email"})).
            .IsUnique()
            .HasDatabaseName($"uq_{tabela}_salao_email");//email não se repete dentro do mesmo salão, mas pode existir em salões diferentes.
        pessoa.ToTable(t =>
        {
            t.HasCheckConstraint($"ck_{tabela}_email_minusculo", "email = lower(email)");
            t.HasCheckConstraint($"ck_{tabela}_email_preenchido", "email <> '' and email = btrim(email)");
            t.HasCheckConstraint($"ck_{tabela}_nome_preenchido", "btrim(nome) <> ''");
            t.HasCheckConstraint($"ck_{tabela}_phone", $"phone ~ {Formatos.PhoneSql}");
        });
    }
}
