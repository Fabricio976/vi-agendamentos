using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ViAgendamentos.Api.GoogleAgenda;

// AES-256-GCM: cifra e autentica. O resultado guarda nonce, tag e texto cifrado, nessa ordem,
// e um byte trocado em qualquer parte faz o Decifrar lançar, em vez de devolver lixo
public sealed class CifraDoToken(IOptions<GoogleAgendaOptions> opcoes)
{
    public const int TamanhoDaChave = 32;
    private static readonly int Nonce = AesGcm.NonceByteSizes.MaxSize;
    private static readonly int Tag = AesGcm.TagByteSizes.MaxSize;

    private readonly byte[] _chave = Convert.FromBase64String(opcoes.Value.ChaveDoToken);

    public static bool ChaveValida(string? chave) =>
        chave is not null
        && Convert.TryFromBase64String(chave, new byte[TamanhoDaChave], out var tamanho)
        && tamanho == TamanhoDaChave;

    public byte[] Cifrar(string token)
    {
        var texto = Encoding.UTF8.GetBytes(token);
        var cifrado = new byte[Nonce + Tag + texto.Length];
        var nonce = cifrado.AsSpan(0, Nonce);
        // Nonce novo a cada cifra: repetir nonce com a mesma chave quebra o GCM.
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_chave, Tag);
        aes.Encrypt(nonce, texto, cifrado.AsSpan(Nonce + Tag), cifrado.AsSpan(Nonce, Tag));
        return cifrado;
    }

    public string Decifrar(byte[] cifrado)
    {
        var texto = new byte[cifrado.Length - Nonce - Tag];
        using var aes = new AesGcm(_chave, Tag);
        aes.Decrypt(cifrado.AsSpan(0, Nonce), cifrado.AsSpan(Nonce + Tag), cifrado.AsSpan(Nonce, Tag), texto);
        return Encoding.UTF8.GetString(texto);
    }
}
