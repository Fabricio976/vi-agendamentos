namespace ViAgendamentos.Api.Tests.Data;

// O mínimo que a API exige para subir. Cada teste acrescenta ou troca o que precisa.
public static class ConfiguracaoDeTeste
{
    public static Dictionary<string, string?> Minima(string conexao) => new()
    {
        ["ConnectionStrings:Default"] = conexao,
        ["Authentication:Google:ClientId"] = "cliente-de-teste",
        ["Authentication:Google:ClientSecret"] = "segredo-de-teste",
    };
}
