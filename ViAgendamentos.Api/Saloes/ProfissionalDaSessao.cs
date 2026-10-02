namespace ViAgendamentos.Api.Saloes;

// A profissional logada no salão da rota, achada pela política de acesso ao salão. Chega ao endpoint como parâmetro,
// e a rota não repete a busca.
public sealed record ProfissionalDaSessao(Guid Id, Guid SalaoId, Role Role)
{
    private static readonly object Chave = new();

    internal static void Guardar(HttpContext http, ProfissionalDaSessao profissional) => http.Items[Chave] = profissional;

    public static ValueTask<ProfissionalDaSessao?> BindAsync(HttpContext http) =>
        ValueTask.FromResult<ProfissionalDaSessao?>(
            http.Items[Chave] as ProfissionalDaSessao
            ?? throw new InvalidOperationException("A rota recebe a profissional da sessão sem passar pela política do salão."));
}
