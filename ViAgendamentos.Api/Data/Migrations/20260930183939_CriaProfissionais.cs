using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ViAgendamentos.Api.Saloes;

#nullable disable

namespace ViAgendamentos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriaProfissionais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:role", "dona,funcionaria");

            migrationBuilder.CreateTable(
                name: "profissionais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<Role>(type: "role", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    aviso_minutos = table.Column<int>(type: "integer", nullable: false),
                    antecedencia_min_minutos = table.Column<int>(type: "integer", nullable: false),
                    janela_dias = table.Column<int>(type: "integer", nullable: false),
                    passo_minutos = table.Column<int>(type: "integer", nullable: false),
                    salao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    sobrenome = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profissionais", x => x.id);
                    table.CheckConstraint("ck_profissionais_email_minusculo", "email = lower(email)");
                    table.CheckConstraint("ck_profissionais_phone", "phone ~ '^55[1-9]{2}[0-9]{8,9}$'");
                    table.ForeignKey(
                        name: "fk_profissionais_salao",
                        column: x => x.salao_id,
                        principalTable: "saloes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_profissionais_salao_email",
                table: "profissionais",
                columns: new[] { "salao_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profissionais");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:Enum:role", "dona,funcionaria");
        }
    }
}
