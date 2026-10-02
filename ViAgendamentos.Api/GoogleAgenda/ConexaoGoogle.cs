using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViAgendamentos.Api.Saloes;

namespace ViAgendamentos.Api.GoogleAgenda;

public enum StatusConexao
{
    Ativa,
    Revogada,
}

public enum SituacaoDaConexao
{
    Desconectada,
    Ativa,
    Revogada,
}

public sealed class ConexaoGoogle
{
    public Guid Id { get; set; }
    public Guid ProfissionalId { get; set; }
    public string GoogleEmail { get; private set; } = null!;
    public byte[] RefreshTokenCifrado { get; private set; } = null!;
    public StatusConexao Status { get; private set; }
    public string? UltimoErro { get; private set; }
    public DateTimeOffset ConectadoEm { get; private set; }

    // Conectar de novo troca o token: a conexão volta inteira a valer.
    public void Conectar(string googleEmail, byte[] refreshTokenCifrado, DateTimeOffset agora)
    {
        GoogleEmail = googleEmail;
        RefreshTokenCifrado = refreshTokenCifrado;
        Status = StatusConexao.Ativa;
        UltimoErro = null;
        ConectadoEm = agora;
    }

    public void Revogar(string erro)
    {
        Status = StatusConexao.Revogada;
        UltimoErro = erro;
    }

    public static SituacaoDaConexao SituacaoDe(ConexaoGoogle? conexao) => conexao?.Status switch
    {
        null => SituacaoDaConexao.Desconectada,
        StatusConexao.Ativa => SituacaoDaConexao.Ativa,
        StatusConexao.Revogada => SituacaoDaConexao.Revogada,
        var status => throw new UnreachableException($"Status de conexão sem situação: {status}."),
    };
}

internal sealed class ConexaoGoogleConfiguration : IEntityTypeConfiguration<ConexaoGoogle>
{
    public void Configure(EntityTypeBuilder<ConexaoGoogle> conexao)
    {
        conexao.HasOne<Profissional>()
            .WithOne()
            .HasForeignKey<ConexaoGoogle>(c => c.ProfissionalId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_conexoes_google_profissional");
        conexao.HasIndex(c => c.ProfissionalId).IsUnique().HasDatabaseName("uq_conexoes_google_profissional");
    }
}
