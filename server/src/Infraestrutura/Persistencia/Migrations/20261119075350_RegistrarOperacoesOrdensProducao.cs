using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarOperacoesOrdensProducao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChaveCancelamento",
                table: "OrdensProducao",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChaveInicio",
                table: "OrdensProducao",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImpressaoInicio",
                table: "OrdensProducao",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomeReceita",
                table: "OrdensProducao",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_EncerradaPorId_ChaveCancelamento",
                table: "OrdensProducao",
                columns: new[] { "EncerradaPorId", "ChaveCancelamento" },
                unique: true,
                filter: "[EncerradaPorId] IS NOT NULL AND [ChaveCancelamento] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_IniciadaPorId_ChaveInicio",
                table: "OrdensProducao",
                columns: new[] { "IniciadaPorId", "ChaveInicio" },
                unique: true,
                filter: "[ChaveInicio] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdensProducao_EncerradaPorId_ChaveCancelamento",
                table: "OrdensProducao");

            migrationBuilder.DropIndex(
                name: "IX_OrdensProducao_IniciadaPorId_ChaveInicio",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "ChaveCancelamento",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "ChaveInicio",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "ImpressaoInicio",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "NomeReceita",
                table: "OrdensProducao");
        }
    }
}
