using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ViAgendamentos.Api.GoogleAgenda;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.GoogleAgenda;

public class CifraDoTokenTests
{
    private const string Token = "1//0g-refresh-token-de-teste";

    private static CifraDoToken NovaCifra() =>
        new(Options.Create(new GoogleAgendaOptions { ChaveDoToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Token_cifrado_volta_igual()
    {
        var cifra = NovaCifra();

        Assert.Equal(Token, cifra.Decifrar(cifra.Cifrar(Token)));
    }

    [Fact]
    public void Cifrar_duas_vezes_o_mesmo_token_da_resultados_diferentes()
    {
        var cifra = NovaCifra();

        Assert.NotEqual(cifra.Cifrar(Token), cifra.Cifrar(Token));
    }

    [Fact]
    public void Token_adulterado_nao_decifra()
    {
        var cifra = NovaCifra();
        var cifrado = cifra.Cifrar(Token);
        cifrado[^1] ^= 1;

        Assert.Throws<AuthenticationTagMismatchException>(() => cifra.Decifrar(cifrado));
    }

    [Fact]
    public void Outra_chave_nao_decifra()
    {
        var cifrado = NovaCifra().Cifrar(Token);

        Assert.Throws<AuthenticationTagMismatchException>(() => NovaCifra().Decifrar(cifrado));
    }

    [Fact]
    public void Cifrado_nao_contem_o_token()
    {
        var cifrado = NovaCifra().Cifrar(Token);

        Assert.Equal(-1, cifrado.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Token)));
        Assert.Equal(-1, cifrado.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(Token)))));
    }

    [Fact]
    public void Api_nao_sobe_sem_a_chave_do_token()
    {
        var erro = SubidaDaApiTests.ErroAoSubir<OptionsValidationException>("Production", ("GoogleAgenda:ChaveDoToken", null));

        Assert.Contains("ChaveDoToken", erro.Message);
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("isto-nao-e-base64")]
    public void Api_nao_sobe_com_chave_de_tamanho_errado(string chave)
    {
        var erro = SubidaDaApiTests.ErroAoSubir<OptionsValidationException>("Production", ("GoogleAgenda:ChaveDoToken", chave));

        Assert.Contains("32 bytes", erro.Message);
    }
}
