using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.Options;
using FabricaDoGoogle = Google.Apis.Http.IHttpClientFactory;

namespace ViAgendamentos.Api.GoogleAgenda;

public static class GoogleAgendaSetup
{
    public const string HttpClientDaAgenda = "google-agenda";
    public const ExponentialBackOffPolicy SemRetentativa = ExponentialBackOffPolicy.None;

    // openid e email trazem o id token, de onde sai o google_email da conexão.
    private static readonly string[] Escopos = [CalendarService.Scope.CalendarEventsOwned, "openid", "email"];

    public static IServiceCollection AddGoogleAgenda(this IServiceCollection servicos)
    {
        servicos
            .AddOptionsWithValidateOnStart<GoogleAgendaOptions>()
            .BindConfiguration(GoogleAgendaOptions.Secao)
            .ValidateDataAnnotations()
            .Validate(
                opcoes => CifraDoToken.ChaveValida(opcoes.ChaveDoToken),
                $"{GoogleAgendaOptions.Secao}:ChaveDoToken precisa ser base64 de {CifraDoToken.TamanhoDaChave} bytes, o tamanho da chave do AES-256.");
        servicos.AddSingleton<CifraDoToken>();

        // A biblioteca do Google monta o próprio HttpClient. O adaptador dela recebe os handlers do IHttpClientFactory que o ASP.NET Core recicla
        // O handler não segue redirecionamento nem descompacta a biblioteca faz os dois por cima dele.
        servicos.AddHttpClient(HttpClientDaAgenda)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        servicos.AddSingleton<FabricaDoGoogle>(provedor =>
        {
            var handlers = provedor.GetRequiredService<IHttpMessageHandlerFactory>();
            return new HttpClientFromMessageHandlerFactory(_ =>
                new(handlers.CreateHandler(HttpClientDaAgenda), performsAutomaticDecompression: false, handlesRedirect: false));
        });

        // O mesmo cliente OAuth do login.
        servicos.AddScoped(provedor =>
        {
            var google = provedor.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = google.ClientId, ClientSecret = google.ClientSecret },
                Scopes = Escopos,
                // A pessoa escolhe de qual conta é a agenda, que pode não ser a do login. E sem o consentimento explícito,
                // quem já autorizou antes volta sem refresh token.
                Prompt = "select_account consent",
                HttpClientFactory = provedor.GetRequiredService<FabricaDoGoogle>(),
                DefaultExponentialBackOffPolicy = SemRetentativa,
            });
        });
        servicos.AddSingleton<StateDaConexao>();
        servicos.AddScoped<VoltaDoGoogle>();
        servicos.AddScoped<AgendaGoogle>();
        return servicos;
    }
}
