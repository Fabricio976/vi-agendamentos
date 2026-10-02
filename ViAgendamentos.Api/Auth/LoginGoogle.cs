using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Pessoas;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Auth;

internal abstract record ResultadoDoLogin;

internal sealed record Entrou(Usuario Usuario) : ResultadoDoLogin;

internal sealed record Recusado(string Motivo) : ResultadoDoLogin;

internal sealed class LoginGoogle(UserManager<Usuario> usuarios, AppDbContext db)
{
    public async Task<ResultadoDoLogin> IdentificarAsync(ExternalLoginInfo externo)
    {
        var usuario = await usuarios.FindByLoginAsync(externo.LoginProvider, externo.ProviderKey);
        if (usuario is null)
        {
            var email = EmailConfirmadoPeloGoogle(externo);
            if (email is null)
            {
                // Sem a confirmação do Google, o e-mail pode ser de outra pessoa
                return new Recusado("email-nao-verificado");
            }

            usuario = await usuarios.FindByEmailAsync(email);
            if (usuario is not null
                && (await usuarios.GetLoginsAsync(usuario)).Any(vinculo => vinculo.LoginProvider == externo.LoginProvider))
            {
                // Outra conta Google com o mesmo e-mail é outra pessoa, como quando o endereço é reatribuído
                return new Recusado("outra-conta-google");
            }

            usuario ??= await CriarAsync(email, externo);
            Garantir(await usuarios.AddLoginAsync(usuario, externo));
        }

        await LigarProfissionaisAsync(usuario);
        return new Entrou(usuario);
    }

    private static string? EmailConfirmadoPeloGoogle(ExternalLoginInfo externo)
    {
        var email = externo.Principal.FindFirstValue(ClaimTypes.Email);
        var verificado = bool.TryParse(externo.Principal.FindFirstValue(AuthSetup.EmailVerificado), out var valor) && valor;
        return email is not null && verificado ? Pessoa.NormalizarEmail(email) : null;
    }

    private async Task<Usuario> CriarAsync(string email, ExternalLoginInfo externo)
    {
        var usuario = new Usuario
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            // O perfil do Google pode vir sem nome aí o email faz as vezes de nome.
            Nome = externo.Principal.FindFirstValue(ClaimTypes.Name) ?? email,
        };
        Garantir(await usuarios.CreateAsync(usuario));
        return usuario;
    }

    // Liga a profissional convidada com este e-mail. Roda em todo login, e não só no primeiro
    private Task LigarProfissionaisAsync(Usuario usuario) =>
        db.Profissionais
            .Where(p => p.UsuarioId == null && p.Email == usuario.Email)
            .ExecuteUpdateAsync(profissional => profissional.SetProperty(p => p.UsuarioId, usuario.Id));

    private static void Garantir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", resultado.Errors.Select(erro => erro.Description)));
        }
    }
}
