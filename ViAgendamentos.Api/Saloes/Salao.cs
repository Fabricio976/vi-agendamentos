using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ViAgendamentos.Api.Saloes;

public sealed class Salao
{
    public Guid Id { get; set; }
    public required string Nome { get; set; }
    public required string Slug { get; set; }
    public string Fuso { get; set; } = "America/Sao_Paulo";
}

internal sealed class SalaoConfiguration : IEntityTypeConfiguration<Salao>
{
    public void Configure(EntityTypeBuilder<Salao> salao)
    {
        salao.HasIndex(s => s.Slug).IsUnique().HasDatabaseName("uq_saloes_slug");
    }
}
