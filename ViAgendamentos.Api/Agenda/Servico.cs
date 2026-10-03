using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViAgendamentos.Api.Saloes;

namespace ViAgendamentos.Api.Agenda;

public sealed class Servico
{
    public Guid Id { get; set; }
    public Guid ProfissionalId { get; set; }
    public required string Nome { get; set; }
    public int DuracaoMinutos { get; set; }
    public bool Ativo { get; set; } = true;
}

internal sealed class ServicoConfiguration : IEntityTypeConfiguration<Servico>
{
    public void Configure(EntityTypeBuilder<Servico> servico)
    {
        servico.HasOne<Profissional>()
            .WithMany()
            .HasForeignKey(s => s.ProfissionalId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_servicos_profissional");
        servico.ToTable(t =>
        {
            t.HasCheckConstraint("ck_servicos_duracao_positiva", "duracao_minutos > 0");
            t.HasCheckConstraint("ck_servicos_nome_preenchido", "btrim(nome) <> ''");
        });
    }
}
