using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViAgendamentos.Api.Pessoas;

namespace ViAgendamentos.Api.Clientes;

// Chamado Origem, e não CriadaPor, porque `atendimentos.origem` usa os mesmos valores.
public enum Origem
{
    Profissional,
    Link,
}

public sealed class Cliente : Pessoa
{
    public required Origem CriadaPor { get; set; }
}

internal sealed class ClienteConfiguration() : PessoaConfiguration<Cliente>("clientes")
{
    public override void Configure(EntityTypeBuilder<Cliente> cliente)
    {
        base.Configure(cliente);
        cliente.ToTable(t => t.HasCheckConstraint(
            "ck_clientes_link_exige_email_e_phone",
            "criada_por <> 'link' or (email is not null and phone is not null)"));
    }
}