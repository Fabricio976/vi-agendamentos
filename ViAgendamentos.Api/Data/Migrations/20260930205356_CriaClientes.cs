using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ViAgendamentos.Api.Clientes;

#nullable disable

namespace ViAgendamentos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriaClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:origem", "link,profissional")
                .Annotation("Npgsql:Enum:role", "dona,funcionaria")
                .OldAnnotation("Npgsql:Enum:role", "dona,funcionaria");

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    criada_por = table.Column<Origem>(type: "origem", nullable: false),
                    salao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    sobrenome = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                    table.CheckConstraint("ck_clientes_email_minusculo", "email = lower(email)");
                    table.CheckConstraint("ck_clientes_email_preenchido", "email <> '' and email = btrim(email)");
                    table.CheckConstraint("ck_clientes_link_exige_email_e_phone", "criada_por <> 'link' or (email is not null and phone is not null)");
                    table.CheckConstraint("ck_clientes_nome_preenchido", "btrim(nome) <> ''");
                    table.CheckConstraint("ck_clientes_phone", "phone ~ '^55[1-9]{2}[0-9]{8,9}$'");
                    table.ForeignKey(
                        name: "fk_clientes_salao",
                        column: x => x.salao_id,
                        principalTable: "saloes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_clientes_salao_email",
                table: "clientes",
                columns: new[] { "salao_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:role", "dona,funcionaria")
                .OldAnnotation("Npgsql:Enum:origem", "link,profissional")
                .OldAnnotation("Npgsql:Enum:role", "dona,funcionaria");
        }
    }
}
