using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Auth;

public sealed record AuthRequirement(bool SoAPropriaProfissional) : IAuthorizationRequirement;

internal sealed class AuthHandler(AppDbContext db, UserManager<Usuario> usuarios) : AuthorizationHandler<AuthRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext contexto, AuthRequirement requisito)
    {
        if (contexto.Resource is not HttpContext http
            || !Guid.TryParse(usuarios.GetUserId(contexto.User), out var usuarioId)
            || !Guid.TryParse(http.GetRouteValue("salaoId") as string, out var salaoId))
        {
            return;
        }

        var profissional = await db.Profissionais
            .Where(p => p.SalaoId == salaoId && p.UsuarioId == usuarioId && p.Ativo)
            .Select(p => new ProfissionalDaSessao(p.Id, p.SalaoId, p.Role))
            .SingleOrDefaultAsync(http.RequestAborted);
        if (profissional is null || (requisito.SoAPropriaProfissional && !RotaDa(http, profissional)))
        {
            return;
        }

        ProfissionalDaSessao.Guardar(http, profissional);
        contexto.Succeed(requisito);
    }

    private static bool RotaDa(HttpContext http, ProfissionalDaSessao profissional) =>
        Guid.TryParse(http.GetRouteValue("profissionalId") as string, out var profissionalId) && profissionalId == profissional.Id;
}
