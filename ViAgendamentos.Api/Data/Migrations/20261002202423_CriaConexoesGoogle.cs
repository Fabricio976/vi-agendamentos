using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ViAgendamentos.Api.GoogleAgenda;

#nullable disable

namespace ViAgendamentos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriaConexoesGoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:origem", "link,profissional")
                .Annotation("Npgsql:Enum:role", "dona,funcionaria")
                .Annotation("Npgsql:Enum:status_conexao", "ativa,revogada")
                .OldAnnotation("Npgsql:Enum:origem", "link,profissional")
                .OldAnnotation("Npgsql:Enum:role", "dona,funcionaria");

            migrationBuilder.CreateTable(
                name: "conexoes_google",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    google_email = table.Column<string>(type: "text", nullable: false),
                    refresh_token_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<StatusConexao>(type: "status_conexao", nullable: false),
                    ultimo_erro = table.Column<string>(type: "text", nullable: true),
                    conectado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conexoes_google", x => x.id);
                    table.ForeignKey(
                        name: "fk_conexoes_google_profissional",
                        column: x => x.profissional_id,
                        principalTable: "profissionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_conexoes_google_profissional",
                table: "conexoes_google",
                column: "profissional_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conexoes_google");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:origem", "link,profissional")
                .Annotation("Npgsql:Enum:role", "dona,funcionaria")
                .OldAnnotation("Npgsql:Enum:origem", "link,profissional")
                .OldAnnotation("Npgsql:Enum:role", "dona,funcionaria")
                .OldAnnotation("Npgsql:Enum:status_conexao", "ativa,revogada");
        }
    }
}
