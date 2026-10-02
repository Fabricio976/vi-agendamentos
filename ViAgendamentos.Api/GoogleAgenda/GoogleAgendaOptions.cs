using System.ComponentModel.DataAnnotations;

namespace ViAgendamentos.Api.GoogleAgenda;

public sealed class GoogleAgendaOptions
{
    public const string Secao = "GoogleAgenda";

    [Required]
    public required string ChaveDoToken { get; set; }
}
