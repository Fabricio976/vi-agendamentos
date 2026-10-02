using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Pessoas;
using ViAgendamentos.Api.Usuarios;

namespace ViAgendamentos.Api.Saloes;

public sealed class SalaoPilotoOptions
{
    public const string Secao = "SalaoPiloto";

    [Required]
    public required string Nome { get; set; }

    [Required]
    public required string Slug { get; set; }

    [Required]
    public required string DonaNome { get; set; }

    [Required]
    [EmailAddress]
    public required string DonaEmail { get; set; }
}

public static class SalaoPiloto
{
    public const string Comando = "salao-piloto";

    public static async Task CadastrarAsync(IServiceProvider servicos, CancellationToken cancelamento = default)
    {
        await using var escopo = servicos.CreateAsyncScope();
        var dados = escopo.ServiceProvider.GetRequiredService<IOptions<SalaoPilotoOptions>>().Value;
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = Pessoa.NormalizarEmail(dados.DonaEmail);
        var usuario = await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(email);

        var salao = new Salao { Nome = dados.Nome, Slug = dados.Slug };
        db.Saloes.Add(salao);
        db.Profissionais.Add(new Profissional
        {
            SalaoId = salao.Id,
            Nome = dados.DonaNome,
            Email = email,
            Role = Role.Dona,
            UsuarioId = usuario?.Id,
        });
        await db.SaveChangesAsync(cancelamento);
    }
}
