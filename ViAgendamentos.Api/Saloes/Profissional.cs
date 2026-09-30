using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViAgendamentos.Api.Pessoas;

namespace ViAgendamentos.Api.Saloes;

public enum Role
{
    Dona,
    Funcionaria,
}

public sealed class Profissional : Pessoa
{
    public required Role Role { get; set; }
    public bool Ativo { get; set; } = true;
    public int AvisoMinutos { get; set; } = 15;
    public int AntecedenciaMinMinutos { get; set; } = 120;
    public int JanelaDias { get; set; } = 30;
    public int PassoMinutos { get; set; } = 15;
}

internal sealed class ProfissionalConfiguration() : PessoaConfiguration<Profissional>("profissionais")
{
    public override void Configure(EntityTypeBuilder<Profissional> profissional)
    {
        base.Configure(profissional);
        profissional.Property(p => p.Email).IsRequired();
    }
}
