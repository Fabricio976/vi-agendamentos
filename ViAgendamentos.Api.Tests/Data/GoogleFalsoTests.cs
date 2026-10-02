using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.WebUtilities;

namespace ViAgendamentos.Api.Tests.Data;

public sealed record ContaGoogle(string Id, string Email, bool EmailVerificado, string Nome)
{
    public static ContaGoogle Nova(string? email = null, bool emailVerificado = true) =>
        new(Guid.NewGuid().ToString("N"), email ?? CadastrosTests.EmailUnico(), emailVerificado, "Pessoa do Google");
}

// Faz o papel dos servidores do Google: o handler do ASP.NET Core troca o código pelo token e busca o perfil
// por este HttpMessageHandler, sem sair da máquina. Qualquer outra chamada lança.
public sealed class GoogleFalsoTests : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, ContaGoogle> _contas = new();

    // O código que o Google entregaria na volta do login desta conta.
    public string CodigoPara(ContaGoogle conta)
    {
        var codigo = Guid.NewGuid().ToString("N");
        _contas[codigo] = conta;
        return codigo;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        // O código vira o próprio access token, e o perfil é achado por ele.
        if (pedido.RequestUri?.AbsoluteUri == GoogleDefaults.TokenEndpoint)
        {
            var formulario = QueryHelpers.ParseQuery(await pedido.Content!.ReadAsStringAsync(cancelamento));
            return Json(new { access_token = formulario["code"].ToString(), token_type = "Bearer", expires_in = 3600 });
        }

        if (pedido.RequestUri?.AbsoluteUri == GoogleDefaults.UserInformationEndpoint)
        {
            var conta = _contas[pedido.Headers.Authorization!.Parameter!];
            return Json(new { sub = conta.Id, email = conta.Email, email_verified = conta.EmailVerificado, name = conta.Nome });
        }

        throw new InvalidOperationException($"Chamada inesperada ao Google: {pedido.Method} {pedido.RequestUri}");
    }

    private static HttpResponseMessage Json(object corpo) => new(HttpStatusCode.OK) { Content = JsonContent.Create(corpo) };
}
