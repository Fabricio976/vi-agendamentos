using System.Buffers.Text;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace ViAgendamentos.Api.Tests.Data;

// Uma conta Google com a agenda principal. O refresh token identifica a conta no dublê, que é um só para a suíte em paralelo.
public sealed record ContaDaAgenda(string RefreshToken, string Email)
{
    public static ContaDaAgenda Nova() => new($"refresh-{Guid.NewGuid():N}", CadastrosTests.EmailUnico());

    public string AccessToken => $"acesso-{RefreshToken}";
}

public sealed record ChamadaAoGoogle(HttpMethod Metodo, Uri Endereco);

// Faz o papel do endpoint de token e da API da Agenda do Google, com o mesmo JSON. Cada conta tem a própria agenda,
// as próprias falhas e o registro das próprias chamadas. Qualquer chamada fora do previsto lança.
public sealed class GoogleAgendaFalsoTests : HttpMessageHandler
{
    public const string EnderecoDeVolta = "https://localhost/api/google/agenda/callback";
    public const int TamanhoDaPagina = 2;
    private const string Eventos = "/calendar/v3/calendars/primary/events";

    private const string EscopoDaAgenda = "https://www.googleapis.com/auth/calendar.events.owned";

    private readonly ConcurrentDictionary<string, (ContaDaAgenda Conta, bool ComRefreshToken, bool ComAgenda)> _codigos = new();
    private readonly ConcurrentDictionary<string, AgendaFalsa> _agendas = new();

    // O código que o Google entregaria na volta da conexão desta conta. Sem a agenda, é a pessoa que desmarcou
    // a permissão da agenda na tela de consentimento.
    public string CodigoPara(ContaDaAgenda conta, bool comRefreshToken = true, bool comAgenda = true)
    {
        Registrar(conta);
        var codigo = Guid.NewGuid().ToString("N");
        _codigos[codigo] = (conta, comRefreshToken, comAgenda);
        return codigo;
    }

    // A conta existe no Google sem passar pela volta da conexão, para quem grava a conexão direto no banco.
    public void Registrar(ContaDaAgenda conta) => _agendas.TryAdd(conta.RefreshToken, new AgendaFalsa());

    public void RevogarRefreshToken(ContaDaAgenda conta) => Agenda(conta).Revogada = true;

    public void FalharToken(ContaDaAgenda conta, HttpStatusCode status) => Agenda(conta).FalhaDoToken = status;

    public void FalharAgenda(ContaDaAgenda conta, HttpMethod metodo, HttpStatusCode status) => Agenda(conta).FalhasDaAgenda[metodo] = status;

    public List<Event> EventosDe(ContaDaAgenda conta) => Agenda(conta).Eventos;

    public IReadOnlyList<ChamadaAoGoogle> ChamadasDe(ContaDaAgenda conta) => [.. Agenda(conta).Chamadas];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        var endereco = pedido.RequestUri!;
        if (endereco.GetLeftPart(UriPartial.Path) == GoogleAuthConsts.OidcTokenUrl)
        {
            return await TokenAsync(pedido, cancelamento);
        }

        if (endereco.Host == "www.googleapis.com" && endereco.AbsolutePath.StartsWith(Eventos))
        {
            return await AgendaAsync(pedido, cancelamento);
        }

        throw new InvalidOperationException($"Chamada inesperada ao Google: {pedido.Method} {endereco}");
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        var formulario = QueryHelpers.ParseQuery(await pedido.Content!.ReadAsStringAsync(cancelamento));
        if (formulario["client_id"] != "cliente-de-teste" || formulario["client_secret"] != "segredo-de-teste")
        {
            throw new InvalidOperationException("Pedido de token sem as credenciais do cliente OAuth de teste.");
        }

        return formulario["grant_type"].ToString() switch
        {
            "authorization_code" => TrocarCodigo(pedido, formulario),
            "refresh_token" => Renovar(pedido, formulario["refresh_token"].ToString()),
            var tipo => throw new InvalidOperationException($"grant_type inesperado: {tipo}"),
        };
    }

    private HttpResponseMessage TrocarCodigo(HttpRequestMessage pedido, Dictionary<string, StringValues> formulario)
    {
        if (formulario["redirect_uri"] != EnderecoDeVolta)
        {
            return ErroDoToken(HttpStatusCode.BadRequest, "redirect_uri_mismatch");
        }

        if (!_codigos.TryRemove(formulario["code"].ToString(), out var emitido))
        {
            return ErroDoToken(HttpStatusCode.BadRequest, "invalid_grant");
        }

        var (conta, comRefreshToken, comAgenda) = emitido;
        Agenda(conta).Chamadas.Enqueue(new(pedido.Method, pedido.RequestUri!));
        return Json(new
        {
            access_token = conta.AccessToken,
            expires_in = 3599,
            token_type = "Bearer",
            scope = $"{(comAgenda ? EscopoDaAgenda + " " : "")}openid https://www.googleapis.com/auth/userinfo.email",
            id_token = IdToken(conta.Email),
            refresh_token = comRefreshToken ? conta.RefreshToken : null,
        });
    }

    private HttpResponseMessage Renovar(HttpRequestMessage pedido, string refreshToken)
    {
        var agenda = _agendas.TryGetValue(refreshToken, out var registrada)
            ? registrada
            : throw new InvalidOperationException($"Refresh token desconhecido no Google falso: {refreshToken}");
        agenda.Chamadas.Enqueue(new(pedido.Method, pedido.RequestUri!));
        if (agenda.FalhaDoToken is { } falha)
        {
            return ErroDoToken(falha, "temporarily_unavailable");
        }

        if (agenda.Revogada)
        {
            return ErroDoToken(HttpStatusCode.BadRequest, "invalid_grant", "Token has been expired or revoked.");
        }

        return Json(new { access_token = $"acesso-{refreshToken}", expires_in = 3599, token_type = "Bearer" });
    }

    private async Task<HttpResponseMessage> AgendaAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        var acesso = pedido.Headers.Authorization?.Parameter
            ?? throw new InvalidOperationException("Chamada à agenda sem o access token.");
        var agenda = acesso.StartsWith("acesso-") && _agendas.TryGetValue(acesso["acesso-".Length..], out var registrada)
            ? registrada
            : throw new InvalidOperationException($"Access token desconhecido no Google falso: {acesso}");
        agenda.Chamadas.Enqueue(new(pedido.Method, pedido.RequestUri!));
        if (agenda.FalhasDaAgenda.TryGetValue(pedido.Method, out var falha))
        {
            return ErroDaAgenda(falha);
        }

        var id = pedido.RequestUri!.AbsolutePath[Eventos.Length..].TrimStart('/');
        if (id == "" && pedido.Method == HttpMethod.Get)
        {
            return Listar(agenda, QueryHelpers.ParseQuery(pedido.RequestUri.Query));
        }

        if (id == "" && pedido.Method == HttpMethod.Post)
        {
            var novo = await LerEventoAsync(pedido, cancelamento);
            novo.Id = Guid.NewGuid().ToString("N");
            agenda.Eventos.Add(novo);
            return Json(novo);
        }

        var evento = agenda.Eventos.SingleOrDefault(e => e.Id == id);
        if (evento is null)
        {
            return ErroDaAgenda(HttpStatusCode.NotFound);
        }

        if (pedido.Method == HttpMethod.Patch)
        {
            // PATCH só troca o que veio: o evento da verificação muda só o título.
            evento.Summary = (await LerEventoAsync(pedido, cancelamento)).Summary ?? evento.Summary;
            return Json(evento);
        }

        if (pedido.Method == HttpMethod.Delete)
        {
            agenda.Eventos.Remove(evento);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        throw new InvalidOperationException($"Chamada inesperada à agenda: {pedido.Method} {pedido.RequestUri}");
    }

    // O Google filtra a janela e expande os recorrentes. O dublê guarda os eventos já como o Google devolveria,
    // então só pagina; os parâmetros da janela são conferidos no registro das chamadas.
    private static HttpResponseMessage Listar(AgendaFalsa agenda, Dictionary<string, StringValues> parametros)
    {
        var inicio = parametros.TryGetValue("pageToken", out var pagina) ? int.Parse(pagina.ToString()) : 0;
        var proxima = inicio + TamanhoDaPagina;
        return Json(new Events
        {
            Items = [.. agenda.Eventos.Skip(inicio).Take(TamanhoDaPagina)],
            NextPageToken = proxima < agenda.Eventos.Count ? proxima.ToString() : null,
        });
    }

    // O id token sem assinatura de verdade: a API lê o e-mail dele sem conferir a assinatura, como a OpenID permite
    // para o token que chega direto do endpoint de token.
    private static string IdToken(string email) => string.Join('.',
        Base64Url.EncodeToString("""{"alg":"RS256","typ":"JWT"}"""u8),
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { iss = "https://accounts.google.com", email, email_verified = true })),
        "assinatura");

    // A biblioteca do Google comprime o corpo em gzip (o GZipEnabled vem ligado), e o Google descomprime.
    private static async Task<Event> LerEventoAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        var corpo = await pedido.Content!.ReadAsStreamAsync(cancelamento);
        await using var json = pedido.Content.Headers.ContentEncoding.Contains("gzip") ? new GZipStream(corpo, CompressionMode.Decompress) : corpo;
        return NewtonsoftJsonSerializer.Instance.Deserialize<Event>(json);
    }

    private static HttpResponseMessage ErroDoToken(HttpStatusCode status, string erro, string? descricao = null) =>
        Json(new { error = erro, error_description = descricao }, status);

    private static HttpResponseMessage ErroDaAgenda(HttpStatusCode status) =>
        Json(new { error = new { code = (int)status, message = "Falha de teste do Google falso" } }, status);

    // O mesmo serializador da biblioteca do Google, com os nomes de campo do JSON dela.
    private static HttpResponseMessage Json(object corpo, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(NewtonsoftJsonSerializer.Instance.Serialize(corpo), Encoding.UTF8, "application/json"),
    };

    private AgendaFalsa Agenda(ContaDaAgenda conta) =>
        _agendas.TryGetValue(conta.RefreshToken, out var agenda)
            ? agenda
            : throw new InvalidOperationException($"Conta {conta.Email} não registrada no Google falso.");

    private sealed class AgendaFalsa
    {
        public bool Revogada { get; set; }
        public HttpStatusCode? FalhaDoToken { get; set; }
        public ConcurrentDictionary<HttpMethod, HttpStatusCode> FalhasDaAgenda { get; } = new();
        public List<Event> Eventos { get; } = [];
        public ConcurrentQueue<ChamadaAoGoogle> Chamadas { get; } = new();
    }
}
