using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViAgendamentos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriaSaloes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saloes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    fuso = table.Column<string>(type: "text", nullable: false),
                    whatsapp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saloes", x => x.id);
                    table.CheckConstraint("ck_saloes_whatsapp", "whatsapp ~ '^55[1-9]{2}[0-9]{8,9}$'");
                });

            migrationBuilder.CreateIndex(
                name: "uq_saloes_slug",
                table: "saloes",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saloes");
        }
    }
}
