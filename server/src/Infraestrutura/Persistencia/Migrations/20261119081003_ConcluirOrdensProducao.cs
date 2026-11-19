using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ConcluirOrdensProducao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChaveConclusao",
                table: "OrdensProducao",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImpressaoConclusao",
                table: "OrdensProducao",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MovimentoAcabadoId",
                table: "OrdensProducao",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeProduzida",
                table: "OrdensProducao",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Versao",
                table: "OrdensProducao",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "MovimentoConsumoId",
                table: "AlocacoesMateriasPrimas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_EncerradaPorId_ChaveConclusao",
                table: "OrdensProducao",
                columns: new[] { "EncerradaPorId", "ChaveConclusao" },
                unique: true,
                filter: "[EncerradaPorId] IS NOT NULL AND [ChaveConclusao] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_MovimentoAcabadoId",
                table: "OrdensProducao",
                column: "MovimentoAcabadoId",
                unique: true,
                filter: "[MovimentoAcabadoId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AlocacoesMateriasPrimas_MovimentoConsumoId",
                table: "AlocacoesMateriasPrimas",
                column: "MovimentoConsumoId",
                unique: true,
                filter: "[MovimentoConsumoId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AlocacoesMateriasPrimas_MovimentosInsumo_MovimentoConsumoId",
                table: "AlocacoesMateriasPrimas",
                column: "MovimentoConsumoId",
                principalTable: "MovimentosInsumo",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrdensProducao_MovimentosProdutoAcabado_MovimentoAcabadoId",
                table: "OrdensProducao",
                column: "MovimentoAcabadoId",
                principalTable: "MovimentosProdutoAcabado",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AlocacoesMateriasPrimas_MovimentosInsumo_MovimentoConsumoId",
                table: "AlocacoesMateriasPrimas");

            migrationBuilder.DropForeignKey(
                name: "FK_OrdensProducao_MovimentosProdutoAcabado_MovimentoAcabadoId",
                table: "OrdensProducao");

            migrationBuilder.DropIndex(
                name: "IX_OrdensProducao_EncerradaPorId_ChaveConclusao",
                table: "OrdensProducao");

            migrationBuilder.DropIndex(
                name: "IX_OrdensProducao_MovimentoAcabadoId",
                table: "OrdensProducao");

            migrationBuilder.DropIndex(
                name: "IX_AlocacoesMateriasPrimas_MovimentoConsumoId",
                table: "AlocacoesMateriasPrimas");

            migrationBuilder.DropColumn(
                name: "ChaveConclusao",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "ImpressaoConclusao",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "MovimentoAcabadoId",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "QuantidadeProduzida",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "Versao",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "MovimentoConsumoId",
                table: "AlocacoesMateriasPrimas");
        }
    }
}
