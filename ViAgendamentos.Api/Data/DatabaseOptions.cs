using System.ComponentModel.DataAnnotations;

namespace ViAgendamentos.Api.Data;

public sealed class DatabaseOptions
{
    public const string Secao = "ConnectionStrings";

    [Required]
    public required string Default { get; set; }
}
