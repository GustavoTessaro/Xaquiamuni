using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AceitarCnpjNoPixManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ConfiguracoesPixManual_Tipo",
                table: "ConfiguracoesPixManual");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConfiguracoesPixManual_Tipo",
                table: "ConfiguracoesPixManual",
                sql: "[TipoChave] IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ConfiguracoesPixManual_Tipo",
                table: "ConfiguracoesPixManual");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConfiguracoesPixManual_Tipo",
                table: "ConfiguracoesPixManual",
                sql: "[TipoChave] IN (1, 2, 3)");
        }
    }
}
