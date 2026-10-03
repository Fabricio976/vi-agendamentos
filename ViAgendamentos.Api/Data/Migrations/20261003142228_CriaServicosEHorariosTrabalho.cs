using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViAgendamentos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriaServicosEHorariosTrabalho : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "horarios_trabalho",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_semana = table.Column<int>(type: "integer", nullable: false),
                    inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    fim = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_horarios_trabalho", x => x.id);
                    table.CheckConstraint("ck_horarios_trabalho_dia_semana", "dia_semana between 0 and 6");
                    table.CheckConstraint("ck_horarios_trabalho_fim_depois_do_inicio", "fim > inicio");
                    table.ForeignKey(
                        name: "fk_horarios_trabalho_profissional",
                        column: x => x.profissional_id,
                        principalTable: "profissionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "servicos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    duracao_minutos = table.Column<int>(type: "integer", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicos", x => x.id);
                    table.CheckConstraint("ck_servicos_duracao_positiva", "duracao_minutos > 0");
                    table.CheckConstraint("ck_servicos_nome_preenchido", "btrim(nome) <> ''");
                    table.ForeignKey(
                        name: "fk_servicos_profissional",
                        column: x => x.profissional_id,
                        principalTable: "profissionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_horarios_trabalho_profissional_id",
                table: "horarios_trabalho",
                column: "profissional_id");

            migrationBuilder.CreateIndex(
                name: "ix_servicos_profissional_id",
                table: "servicos",
                column: "profissional_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "horarios_trabalho");

            migrationBuilder.DropTable(
                name: "servicos");
        }
    }
}
