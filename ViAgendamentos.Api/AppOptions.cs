using System.ComponentModel.DataAnnotations;

namespace ViAgendamentos.Api;

public sealed class AppOptions
{
    public const string Secao = "App";

    [Required]
    public required string Nome { get; set; }
}
