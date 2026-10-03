using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ViAgendamentos.Api.Data;

namespace ViAgendamentos.Api.Agenda;

public sealed record ServicoNaEntrada(
    [Required(ErrorMessage = "Informe o nome do serviço.")]
    [MaxLength(80, ErrorMessage = "O nome do serviço tem até 80 caracteres.")]
    string Nome,
    [Range(5, 480, ErrorMessage = "A duração vai de 5 a 480 minutos.")] int DuracaoMinutos,
    bool Ativo = true);

public sealed record ServicoDaProfissional(Guid Id, string Nome, int DuracaoMinutos, bool Ativo)
{
    public static ServicoDaProfissional De(Servico servico) => new(servico.Id, servico.Nome, servico.DuracaoMinutos, servico.Ativo);
}

public static class ServicosEndpoints
{
    public static RouteGroupBuilder MapServicos(this RouteGroupBuilder profissional)
    {
        var servicos = profissional.MapGroup("/servicos");

        servicos.MapGet("/", (Guid profissionalId, AppDbContext db, CancellationToken cancelamento) =>
            db.Servicos
                .Where(s => s.ProfissionalId == profissionalId)
                .OrderBy(s => s.Nome)
                .Select(s => ServicoDaProfissional.De(s))
                .ToListAsync(cancelamento));

        servicos.MapPost("/", async (Guid profissionalId, ServicoNaEntrada entrada, AppDbContext db, CancellationToken cancelamento) =>
        {
            var servico = new Servico
            {
                ProfissionalId = profissionalId,
                Nome = entrada.Nome,
                DuracaoMinutos = entrada.DuracaoMinutos,
                Ativo = entrada.Ativo,
            };
            db.Servicos.Add(servico);
            await db.SaveChangesAsync(cancelamento);
            return TypedResults.Created((string?)null, ServicoDaProfissional.De(servico));
        });

        servicos.MapPut("/{servicoId:guid}", async Task<Results<Ok<ServicoDaProfissional>, NotFound>> (
            Guid profissionalId, Guid servicoId, ServicoNaEntrada entrada, AppDbContext db, CancellationToken cancelamento) =>
        {
            var servico = await db.Servicos.SingleOrDefaultAsync(s => s.Id == servicoId && s.ProfissionalId == profissionalId, cancelamento);
            if (servico is null)
            {
                return TypedResults.NotFound();
            }

            db.Entry(servico).CurrentValues.SetValues(entrada);
            await db.SaveChangesAsync(cancelamento);
            return TypedResults.Ok(ServicoDaProfissional.De(servico));
        });

        return profissional;
    }
}
