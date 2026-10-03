using System.Net;
using System.Net.Http.Json;

namespace ViAgendamentos.Api.Tests.Data;

internal sealed record ProblemaDeValidacao(Dictionary<string, string[]> Errors);

public static class ErroDeValidacaoTests
{
    // 400 no formato do ValidationProblem. Devolve as mensagens do campo, para o teste conferir o texto.
    public static async Task<string[]> NoCampoAsync(HttpResponseMessage resposta, string campo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var problema = await resposta.Content.ReadFromJsonAsync<ProblemaDeValidacao>(TestContext.Current.CancellationToken);
        Assert.NotNull(problema);
        Assert.Contains(campo, problema.Errors.Keys);
        return problema.Errors[campo];
    }
}
