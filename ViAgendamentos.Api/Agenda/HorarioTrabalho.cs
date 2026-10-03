using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViAgendamentos.Api.Saloes;

namespace ViAgendamentos.Api.Agenda;

// Uma faixa de trabalho num dia da semana. Duas faixas no mesmo dia deixam o almoço entre elas.
public sealed class HorarioTrabalho
{
    public Guid Id { get; set; }
    public Guid ProfissionalId { get; set; }
    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly Inicio { get; set; }
    public TimeOnly Fim { get; set; }
}

internal sealed class HorarioTrabalhoConfiguration : IEntityTypeConfiguration<HorarioTrabalho>
{
    public void Configure(EntityTypeBuilder<HorarioTrabalho> faixa)
    {
        faixa.HasOne<Profissional>()
            .WithMany()
            .HasForeignKey(h => h.ProfissionalId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_horarios_trabalho_profissional");
        faixa.ToTable(t =>
        {
            t.HasCheckConstraint("ck_horarios_trabalho_fim_depois_do_inicio", "fim > inicio");
            t.HasCheckConstraint("ck_horarios_trabalho_dia_semana", "dia_semana between 0 and 6");
        });
    }
}
