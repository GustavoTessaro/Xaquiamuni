using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class IdempotenciaCriacaoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChaveIdempotencia",
                table: "Pedidos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_ClienteId_ChaveIdempotencia",
                table: "Pedidos",
                columns: new[] { "ClienteId", "ChaveIdempotencia" },
                unique: true,
                filter: "[ChaveIdempotencia] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pedidos_ClienteId_ChaveIdempotencia",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "ChaveIdempotencia",
                table: "Pedidos");
        }
    }
}
