using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ConverterCotacaoCorreiosEmPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos");

            migrationBuilder.AddColumn<Guid>(
                name: "CotacaoCorreiosId",
                table: "Pedidos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisaoCotacaoCorreios",
                table: "Pedidos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CotacaoCorreiosId",
                table: "Pedidos",
                column: "CotacaoCorreiosId",
                unique: true,
                filter: "[CotacaoCorreiosId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CotacaoCorreiosId_RevisaoCotacaoCorreios",
                table: "Pedidos",
                columns: new[] { "CotacaoCorreiosId", "RevisaoCotacaoCorreios" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos",
                sql: "((([ModalidadeEntrega] = 1 AND [FreteCentavos] = 0) OR ([ModalidadeEntrega] = 2 AND [Entrega] IS NOT NULL)) AND [CotacaoCorreiosId] IS NULL AND [RevisaoCotacaoCorreios] IS NULL) OR ([ModalidadeEntrega] = 3 AND [Entrega] IS NOT NULL AND [CotacaoCorreiosId] IS NOT NULL AND [RevisaoCotacaoCorreios] IS NOT NULL AND [RevisaoCotacaoCorreios] > 0 AND [RetiradaAutorizadaEmUtc] IS NULL AND [Situacao] NOT IN (4, 5, 8))");

            migrationBuilder.AddForeignKey(
                name: "FK_Pedidos_PropostasCorreios_CotacaoCorreiosId_RevisaoCotacaoCorreios",
                table: "Pedidos",
                columns: new[] { "CotacaoCorreiosId", "RevisaoCotacaoCorreios" },
                principalTable: "PropostasCorreios",
                principalColumns: new[] { "SolicitacaoId", "Revisao" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedidos_PropostasCorreios_CotacaoCorreiosId_RevisaoCotacaoCorreios",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_CotacaoCorreiosId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_CotacaoCorreiosId_RevisaoCotacaoCorreios",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "CotacaoCorreiosId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "RevisaoCotacaoCorreios",
                table: "Pedidos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos",
                sql: "([ModalidadeEntrega] = 1 AND [FreteCentavos] = 0) OR ([ModalidadeEntrega] = 2 AND [Entrega] IS NOT NULL)");
        }
    }
}
