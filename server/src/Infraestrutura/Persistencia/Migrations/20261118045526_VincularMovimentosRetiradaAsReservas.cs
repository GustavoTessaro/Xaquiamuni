using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class VincularMovimentosRetiradaAsReservas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservaProdutoAcabadoId",
                table: "MovimentosProdutoAcabado",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ReservasProdutoAcabado_Id_ProdutoVarianteId_LoteId_Quantidade",
                table: "ReservasProdutoAcabado",
                columns: new[] { "Id", "ProdutoVarianteId", "LoteId", "Quantidade" });

            migrationBuilder.CreateIndex(
                name: "UX_MovimentosProdutoAcabado_Reserva_Retirada",
                table: "MovimentosProdutoAcabado",
                columns: new[] { "ReservaProdutoAcabadoId", "ProdutoVarianteId", "LoteId", "Quantidade" },
                unique: true,
                filter: "[ReservaProdutoAcabadoId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosProdutoAcabado_Reserva_SaidaVendavel",
                table: "MovimentosProdutoAcabado",
                sql: "[ReservaProdutoAcabadoId] IS NULL OR ([Tipo] = 3 AND [CondicaoOrigem] IS NOT NULL AND [CondicaoOrigem] = 1 AND [CondicaoDestino] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosProdutoAcabado_Reserva_Retirada",
                table: "MovimentosProdutoAcabado",
                columns: new[] { "ReservaProdutoAcabadoId", "ProdutoVarianteId", "LoteId", "Quantidade" },
                principalTable: "ReservasProdutoAcabado",
                principalColumns: new[] { "Id", "ProdutoVarianteId", "LoteId", "Quantidade" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosProdutoAcabado_Reserva_Retirada",
                table: "MovimentosProdutoAcabado");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ReservasProdutoAcabado_Id_ProdutoVarianteId_LoteId_Quantidade",
                table: "ReservasProdutoAcabado");

            migrationBuilder.DropIndex(
                name: "UX_MovimentosProdutoAcabado_Reserva_Retirada",
                table: "MovimentosProdutoAcabado");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosProdutoAcabado_Reserva_SaidaVendavel",
                table: "MovimentosProdutoAcabado");

            migrationBuilder.DropColumn(
                name: "ReservaProdutoAcabadoId",
                table: "MovimentosProdutoAcabado");
        }
    }
}
