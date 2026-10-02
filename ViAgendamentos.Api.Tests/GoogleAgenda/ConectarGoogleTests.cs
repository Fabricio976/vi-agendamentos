using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ViAgendamentos.Api.GoogleAgenda;
using ViAgendamentos.Api.Saloes;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.GoogleAgenda;

// A resposta do status como o front lê: a situação chega como texto.
internal sealed record ConexaoNaResposta(string Status, string? GoogleEmail, DateTimeOffset? ConectadoEm, string? UltimoErro);

public class ConectarGoogleTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static string Tela(ProfissionalLogada logada, string resultado) => $"/saloes/{logada.Salao.Id}/google?resultado={resultado}";

    private static string Volta(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.Redirect, resposta.StatusCode);
        return resposta.Headers.Location!.OriginalString;
    }

    private async Task<ConexaoGoogle?> ConexaoAsync(Guid profissionalId)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        return await db.ConexoesGoogle.SingleOrDefaultAsync(c => c.ProfissionalId == profissionalId, Cancelamento);
    }

    private async Task RevogarNoBancoAsync(Guid profissionalId)
    {
        await using var escopo = banco.NovoEscopo(out var db);
        var conexao = await db.ConexoesGoogle.SingleAsync(c => c.ProfissionalId == profissionalId, Cancelamento);
        conexao.Revogar("invalid_grant: Token has been expired or revoked.");
        await db.SaveChangesAsync(Cancelamento);
    }

    private string Decifrar(byte[] cifrado) => banco.Api.Services.GetRequiredService<CifraDoToken>().Decifrar(cifrado);

    [Fact]
    public async Task Conectar_leva_ao_google_com_escopo_da_agenda_offline_escolha_de_conta_e_consentimento()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var ida = await logada.Navegador.Http.GetAsync($"{logada.Rota}/google/conectar", Cancelamento);

        Assert.Equal(HttpStatusCode.Redirect, ida.StatusCode);
        var endereco = ida.Headers.Location!;
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", endereco.GetLeftPart(UriPartial.Path));
        var parametros = QueryHelpers.ParseQuery(endereco.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
        Assert.Equal("code", parametros.GetValueOrDefault("response_type"));
        Assert.Equal("cliente-de-teste", parametros.GetValueOrDefault("client_id"));
        Assert.Equal(GoogleAgendaFalsoTests.EnderecoDeVolta, parametros.GetValueOrDefault("redirect_uri"));
        Assert.Equal(
            ["email", "https://www.googleapis.com/auth/calendar.events.owned", "openid"],
            parametros.GetValueOrDefault("scope")?.Split(' ').Order(StringComparer.Ordinal));
        Assert.Equal("offline", parametros.GetValueOrDefault("access_type"));
        Assert.Equal(["consent", "select_account"], parametros.GetValueOrDefault("prompt")?.Split(' ').Order(StringComparer.Ordinal));
        Assert.False(string.IsNullOrEmpty(parametros.GetValueOrDefault("state")));
    }

    [Fact]
    public async Task Conectar_a_agenda_de_outra_profissional_responde_403()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        Profissional outra;
        await using (var escopo = banco.NovoEscopo(out var db))
        {
            outra = await CadastrosTests.ProfissionalAsync(db, logada.Salao.Id, Role.Funcionaria);
        }

        var ida = await logada.Navegador.Http.GetAsync(
            $"/api/saloes/{logada.Salao.Id}/profissionais/{outra.Id}/google/conectar", Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, ida.StatusCode);
    }

    [Fact]
    public async Task Volta_do_google_grava_o_token_cifrado_e_a_conexao_ativa()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var conta = ContaDaAgenda.Nova();

        var volta = await logada.Navegador.ConectarGoogleAgendaAsync(banco.GoogleAgenda, logada.Salao.Id, logada.Profissional.Id, conta);

        Assert.Equal(Tela(logada, "conectada"), Volta(volta));
        var conexao = await ConexaoAsync(logada.Profissional.Id);
        Assert.NotNull(conexao);
        Assert.Equal(StatusConexao.Ativa, conexao.Status);
        Assert.Equal(conta.Email, conexao.GoogleEmail);
        Assert.Equal(-1, conexao.RefreshTokenCifrado.AsSpan().IndexOf(Encoding.UTF8.GetBytes(conta.RefreshToken)));
        Assert.Equal(conta.RefreshToken, Decifrar(conexao.RefreshTokenCifrado));
    }

    [Fact]
    public async Task Volta_com_state_de_outra_sessao_e_recusada_sem_gravar()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        using var outraPessoa = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var state = await logada.Navegador.IrAoGoogleAgendaAsync(logada.Salao.Id, logada.Profissional.Id);
        var conta = ContaDaAgenda.Nova();

        var volta = await outraPessoa.Navegador.VoltarDoGoogleAgendaAsync(
            $"code={banco.GoogleAgenda.CodigoPara(conta)}&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.BadRequest, volta.StatusCode);
        Assert.Null(await ConexaoAsync(logada.Profissional.Id));
        Assert.Empty(banco.GoogleAgenda.ChamadasDe(conta));
    }

    [Fact]
    public async Task Volta_com_state_adulterado_e_recusada()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var state = await logada.Navegador.IrAoGoogleAgendaAsync(logada.Salao.Id, logada.Profissional.Id);
        var meio = state.Length / 2;
        var adulterado = state[..meio] + (state[meio] == 'A' ? 'B' : 'A') + state[(meio + 1)..];

        var volta = await logada.Navegador.VoltarDoGoogleAgendaAsync(
            $"code={banco.GoogleAgenda.CodigoPara(ContaDaAgenda.Nova())}&state={Uri.EscapeDataString(adulterado)}");

        Assert.Equal(HttpStatusCode.BadRequest, volta.StatusCode);
        Assert.Null(await ConexaoAsync(logada.Profissional.Id));
    }

    // O state nasce numa API com o relógio do teste, e a mesma sessão volta 16 minutos depois.
    [Fact]
    public async Task Volta_com_state_vencido_volta_para_a_tela_sem_conectar()
    {
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var apiComRelogio = banco.Api.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(servicos => servicos.AddSingleton<TimeProvider>(relogio)));
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        using var navegador = new NavegadorTests(apiComRelogio);
        navegador.CopiarSessaoDe(logada.Navegador);
        var state = await navegador.IrAoGoogleAgendaAsync(logada.Salao.Id, logada.Profissional.Id);
        relogio.Advance(TimeSpan.FromMinutes(16));

        var volta = await navegador.VoltarDoGoogleAgendaAsync(
            $"code={banco.GoogleAgenda.CodigoPara(ContaDaAgenda.Nova())}&state={Uri.EscapeDataString(state)}");

        Assert.Equal(Tela(logada, "expirado"), Volta(volta));
        Assert.Null(await ConexaoAsync(logada.Profissional.Id));
    }

    [Fact]
    public async Task Desistir_no_google_volta_para_a_tela_da_conexao_cancelada()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var state = await logada.Navegador.IrAoGoogleAgendaAsync(logada.Salao.Id, logada.Profissional.Id);

        var volta = await logada.Navegador.VoltarDoGoogleAgendaAsync($"error=access_denied&state={Uri.EscapeDataString(state)}");

        Assert.Equal(Tela(logada, "cancelada"), Volta(volta));
        Assert.Null(await ConexaoAsync(logada.Profissional.Id));
    }

    [Fact]
    public async Task Google_sem_refresh_token_na_resposta_nao_conecta_e_avisa()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var state = await logada.Navegador.IrAoGoogleAgendaAsync(logada.Salao.Id, logada.Profissional.Id);

        var volta = await logada.Navegador.VoltarDoGoogleAgendaAsync(
            $"code={banco.GoogleAgenda.CodigoPara(ContaDaAgenda.Nova(), comRefreshToken: false)}&state={Uri.EscapeDataString(state)}");

        Assert.Equal(Tela(logada, "erro"), Volta(volta));
        Assert.Null(await ConexaoAsync(logada.Profissional.Id));
    }

    [Fact]
    public async Task Google_sem_a_permissao_da_agenda_nao_conecta_e_pede_a_permissao()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var state = await logada.Navegador.IrAoGoogleAgendaAsync(logada.Salao.Id, logada.Profissional.Id);

        var volta = await logada.Navegador.VoltarDoGoogleAgendaAsync(
            $"code={banco.GoogleAgenda.CodigoPara(ContaDaAgenda.Nova(), comAgenda: false)}&state={Uri.EscapeDataString(state)}");

        Assert.Equal(Tela(logada, "sem-permissao"), Volta(volta));
        Assert.Null(await ConexaoAsync(logada.Profissional.Id));
    }

    [Fact]
    public async Task Reconectar_troca_o_token_e_volta_a_ativa()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        await logada.Navegador.ConectarGoogleAgendaAsync(banco.GoogleAgenda, logada.Salao.Id, logada.Profissional.Id, ContaDaAgenda.Nova());
        await RevogarNoBancoAsync(logada.Profissional.Id);
        var nova = ContaDaAgenda.Nova();

        var volta = await logada.Navegador.ConectarGoogleAgendaAsync(banco.GoogleAgenda, logada.Salao.Id, logada.Profissional.Id, nova);

        Assert.Equal(Tela(logada, "conectada"), Volta(volta));
        var conexao = await ConexaoAsync(logada.Profissional.Id);
        Assert.NotNull(conexao);
        Assert.Equal(StatusConexao.Ativa, conexao.Status);
        Assert.Null(conexao.UltimoErro);
        Assert.Equal(nova.Email, conexao.GoogleEmail);
        Assert.Equal(nova.RefreshToken, Decifrar(conexao.RefreshTokenCifrado));
    }

    [Fact]
    public async Task Status_sem_conexao_e_desconectada()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);

        var status = await logada.Navegador.Http.GetFromJsonAsync<ConexaoNaResposta>($"{logada.Rota}/google", Cancelamento);

        Assert.Equal(new ConexaoNaResposta("desconectada", null, null, null), status);
    }

    [Fact]
    public async Task Status_mostra_ativa_com_o_email_do_google()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var conta = ContaDaAgenda.Nova();
        await logada.Navegador.ConectarGoogleAgendaAsync(banco.GoogleAgenda, logada.Salao.Id, logada.Profissional.Id, conta);
        var conexao = await ConexaoAsync(logada.Profissional.Id);

        var status = await logada.Navegador.Http.GetFromJsonAsync<ConexaoNaResposta>($"{logada.Rota}/google", Cancelamento);

        Assert.Equal(new ConexaoNaResposta("ativa", conta.Email, conexao!.ConectadoEm, null), status);
    }

    [Fact]
    public async Task Status_mostra_revogada_com_o_ultimo_erro()
    {
        using var logada = await CadastrosTests.ProfissionalLogadaAsync(banco);
        var conta = ContaDaAgenda.Nova();
        await logada.Navegador.ConectarGoogleAgendaAsync(banco.GoogleAgenda, logada.Salao.Id, logada.Profissional.Id, conta);
        await RevogarNoBancoAsync(logada.Profissional.Id);
        var conexao = await ConexaoAsync(logada.Profissional.Id);

        var status = await logada.Navegador.Http.GetFromJsonAsync<ConexaoNaResposta>($"{logada.Rota}/google", Cancelamento);

        Assert.Equal(
            new ConexaoNaResposta("revogada", conta.Email, conexao!.ConectadoEm, "invalid_grant: Token has been expired or revoked."),
            status);
    }
}
