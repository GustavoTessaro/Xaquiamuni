using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CongelarDescontoFretePostal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescontoFrete",
                table: "PropostasCorreios",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SubtotalProdutosCentavos",
                table: "PropostasCorreios",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PropostasCorreios_Subtotal",
                table: "PropostasCorreios",
                sql: "([SubtotalProdutosCentavos] IS NULL AND [DescontoFrete] IS NULL) OR ([SubtotalProdutosCentavos] IS NOT NULL AND [SubtotalProdutosCentavos] >= 0 AND [SubtotalProdutosCentavos] <= 9223372036854775807 - [FreteCentavos])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PropostasCorreios_Subtotal",
                table: "PropostasCorreios");

            migrationBuilder.DropColumn(
                name: "DescontoFrete",
                table: "PropostasCorreios");

            migrationBuilder.DropColumn(
                name: "SubtotalProdutosCentavos",
                table: "PropostasCorreios");
        }
    }
}
