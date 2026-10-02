using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ViAgendamentos.Api.Data;
using ViAgendamentos.Api.Tests.Data;

namespace ViAgendamentos.Api.Tests.Auth;

public class ChavesDaSessaoTests(PostgresFixtureTests banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tabela_chaves_protecao_tem_colunas_em_snake_case()
    {
        var colunas = await banco.ColunasAsync("chaves_protecao");

        Assert.Equal(new[] { "friendly_name", "id", "xml" }, colunas);
    }

    [Fact]
    public async Task Sessao_aberta_numa_instancia_vale_em_outra_que_le_as_mesmas_chaves_do_banco()
    {
        using var navegador = banco.NovoNavegador();
        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova());
        using var outraInstancia = banco.ApiCom();
        using var outroNavegador = new NavegadorTests(outraInstancia);
        outroNavegador.CopiarSessaoDe(navegador);

        var resposta = await outroNavegador.Http.GetAsync("/api/auth/me", Cancelamento);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    // Uma versão nova da imagem pode instalar a API em outra pasta, e a sessão aberta antes continua valendo.
    [Fact]
    public async Task Sessao_vale_na_instancia_instalada_em_outra_pasta()
    {
        using var navegador = banco.NovoNavegador();
        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova());
        using var outraPasta = banco.Api.WithWebHostBuilder(builder => builder.UseContentRoot(AppContext.BaseDirectory));
        using var outroNavegador = new NavegadorTests(outraPasta);
        outroNavegador.CopiarSessaoDe(navegador);

        var resposta = await outroNavegador.Http.GetAsync("/api/auth/me", Cancelamento);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Instancia_com_outras_chaves_recusa_a_sessao()
    {
        using var navegador = banco.NovoNavegador();
        await navegador.EntrarComGoogleAsync(banco.Google, ContaGoogle.Nova());
        using var outraInstancia = banco.ApiCom(("ConnectionStrings:Default", banco.ConexaoComOutroBanco()));
        await using (var escopo = outraInstancia.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(Cancelamento);
        }
        using var outroNavegador = new NavegadorTests(outraInstancia);
        outroNavegador.CopiarSessaoDe(navegador);

        var resposta = await outroNavegador.Http.GetAsync("/api/auth/me", Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
