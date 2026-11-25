using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarEmailComercial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailComercial",
                table: "ConfiguracoesAtendimento",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailComercialHabilitado",
                table: "ConfiguracoesAtendimento",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConfiguracoesAtendimento_EmailComercial",
                table: "ConfiguracoesAtendimento",
                sql: "[EmailComercialHabilitado] = 0 OR [EmailComercial] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ConfiguracoesAtendimento_EmailComercial",
                table: "ConfiguracoesAtendimento");

            migrationBuilder.DropColumn(
                name: "EmailComercial",
                table: "ConfiguracoesAtendimento");

            migrationBuilder.DropColumn(
                name: "EmailComercialHabilitado",
                table: "ConfiguracoesAtendimento");
        }
    }
}
