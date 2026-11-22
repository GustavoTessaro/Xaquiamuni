using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarEntregaCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos");

            migrationBuilder.AddColumn<string>(
                name: "Entrega",
                table: "Pedidos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FreteCentavos",
                table: "Pedidos",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "HashOfertaEntrega",
                table: "Pedidos",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SubtotalProdutosCentavos",
                table: "Pedidos",
                type: "bigint",
                nullable: false,
                computedColumnSql: "[TotalCentavos] - [FreteCentavos]",
                stored: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Frete_Valid",
                table: "Pedidos",
                sql: "[FreteCentavos] >= 0 AND [FreteCentavos] <= [TotalCentavos]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos",
                sql: "([ModalidadeEntrega] = 1 AND [FreteCentavos] = 0) OR ([ModalidadeEntrega] = 2 AND [Entrega] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Frete_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "SubtotalProdutosCentavos",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "Entrega",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "FreteCentavos",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "HashOfertaEntrega",
                table: "Pedidos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos",
                sql: "[ModalidadeEntrega] = 1");
        }
    }
}
