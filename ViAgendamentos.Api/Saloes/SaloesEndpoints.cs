using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Saloes;

public sealed record SalaoDaProfissional(Guid Id, string Nome, string Slug, Role Role);

public static class SaloesEndpoints
{
    public static RouteGroupBuilder MapSaloes(this RouteGroupBuilder api)
    {
        // Só os salões em que a pessoa logada é profissional o filtro pelo usuário é a autorização desta rota.
        api.MapGet("/saloes", async (ClaimsPrincipal principal, UserManager<Usuario> usuarios, AppDbContext db, CancellationToken cancelamento) =>
        {
            var usuarioId = Guid.Parse(usuarios.GetUserId(principal)!);
            return await (
                    from p in db.Profissionais
                    where p.UsuarioId == usuarioId
                    join s in db.Saloes on p.SalaoId equals s.Id
                    orderby s.Nome
                    select new SalaoDaProfissional(s.Id, s.Nome, s.Slug, p.Role))
                .ToListAsync(cancelamento);
        });

        return api;
    }
}
