using Microsoft.AspNetCore.Identity;

namespace ViAgendamentos.Api.Usuarios;

// Sem senha: a pessoa entra pelo Google ou por código no e-mail, e o PasswordHash fica sempre vazio.
public sealed class Usuario : IdentityUser<Guid>
{
    public required string Nome { get; set; }
}
