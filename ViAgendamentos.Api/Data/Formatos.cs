namespace ViAgendamentos.Api.Data;

internal static class Formatos
{
    // 55, DDD sem zero e 8 ou 9 dígitos, só números: o formato que o link do WhatsApp espera.
    public const string PhoneSql = "'^55[1-9]{2}[0-9]{8,9}$'";
}
