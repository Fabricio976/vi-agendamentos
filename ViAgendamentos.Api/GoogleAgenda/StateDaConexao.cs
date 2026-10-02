using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ViAgendamentos.Api.GoogleAgenda;

public sealed record PedidoDeConexao(Guid UsuarioId, Guid SalaoId, Guid ProfissionalId, DateTimeOffset Validade);

// O state vai ao Google e volta na URL: o Data Protection cifra e assina, e só esta API lê ou forja um.
// A validade vai dentro dele, comparada pelo TimeProvider. O ITimeLimitedDataProtector compara com o relógio da máquina
// e, vencido, não deixa ler o salão para devolver a pessoa à tela certa.
public sealed class StateDaConexao(IDataProtectionProvider protecao, TimeProvider relogio)
{
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(15);
    private readonly IDataProtector _protetor = protecao.CreateProtector("GoogleAgenda.State");

    public string Emitir(Guid usuarioId, Guid salaoId, Guid profissionalId) =>
        _protetor.Protect(JsonSerializer.Serialize(new PedidoDeConexao(usuarioId, salaoId, profissionalId, relogio.GetUtcNow() + Validade)));

    // Adulterado ou cifrado por outra chave: o Data Protection lança CryptographicException nos dois casos.
    public PedidoDeConexao? Ler(string state)
    {
        try
        {
            return JsonSerializer.Deserialize<PedidoDeConexao>(_protetor.Unprotect(state));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public bool Venceu(PedidoDeConexao pedido) => relogio.GetUtcNow() > pedido.Validade;
}
