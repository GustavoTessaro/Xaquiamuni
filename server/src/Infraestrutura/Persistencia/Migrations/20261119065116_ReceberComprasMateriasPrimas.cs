using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ReceberComprasMateriasPrimas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChaveRecebimento",
                table: "ComprasMateriasPrimas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImpressaoRecebimento",
                table: "ComprasMateriasPrimas",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecebidaEmUtc",
                table: "ComprasMateriasPrimas",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecebidaPorId",
                table: "ComprasMateriasPrimas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EntradasRecebimentoCompra",
                columns: table => new
                {
                    ComponenteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinhaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntradasRecebimentoCompra", x => x.ComponenteId);
                    table.ForeignKey(
                        name: "FK_EntradasRecebimentoCompra_ComponentesCompraMateriaPrima_ComponenteId",
                        column: x => x.ComponenteId,
                        principalTable: "ComponentesCompraMateriaPrima",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntradasRecebimentoCompra_ComprasMateriasPrimas_CompraId",
                        column: x => x.CompraId,
                        principalTable: "ComprasMateriasPrimas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntradasRecebimentoCompra_LinhasCompraMateriaPrima_LinhaId",
                        column: x => x.LinhaId,
                        principalTable: "LinhasCompraMateriaPrima",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntradasRecebimentoCompra_MovimentosInsumo_MovimentoId",
                        column: x => x.MovimentoId,
                        principalTable: "MovimentosInsumo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprasMateriasPrimas_RecebidaPorId_ChaveRecebimento",
                table: "ComprasMateriasPrimas",
                columns: new[] { "RecebidaPorId", "ChaveRecebimento" },
                unique: true,
                filter: "[RecebidaPorId] IS NOT NULL AND [ChaveRecebimento] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntradasRecebimentoCompra_CompraId_Ordem",
                table: "EntradasRecebimentoCompra",
                columns: new[] { "CompraId", "Ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntradasRecebimentoCompra_LinhaId",
                table: "EntradasRecebimentoCompra",
                column: "LinhaId");

            migrationBuilder.CreateIndex(
                name: "IX_EntradasRecebimentoCompra_MovimentoId",
                table: "EntradasRecebimentoCompra",
                column: "MovimentoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntradasRecebimentoCompra");

            migrationBuilder.DropIndex(
                name: "IX_ComprasMateriasPrimas_RecebidaPorId_ChaveRecebimento",
                table: "ComprasMateriasPrimas");

            migrationBuilder.DropColumn(
                name: "ChaveRecebimento",
                table: "ComprasMateriasPrimas");

            migrationBuilder.DropColumn(
                name: "ImpressaoRecebimento",
                table: "ComprasMateriasPrimas");

            migrationBuilder.DropColumn(
                name: "RecebidaEmUtc",
                table: "ComprasMateriasPrimas");

            migrationBuilder.DropColumn(
                name: "RecebidaPorId",
                table: "ComprasMateriasPrimas");
        }
    }
}
