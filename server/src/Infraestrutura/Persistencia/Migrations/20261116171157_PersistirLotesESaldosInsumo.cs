using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PersistirLotesESaldosInsumo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LotesInsumo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Identificacao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataEntrada = table.Column<DateOnly>(type: "date", nullable: false),
                    DataValidade = table.Column<DateOnly>(type: "date", nullable: true),
                    Fornecedor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Bloqueado = table.Column<bool>(type: "bit", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesInsumo", x => x.Id);
                    table.UniqueConstraint("AK_LotesInsumo_InsumoId_Id", x => new { x.InsumoId, x.Id });
                    table.CheckConstraint("CK_LotesInsumo_Fornecedor_NotBlank", "[Fornecedor] IS NULL OR LEN(LTRIM(RTRIM([Fornecedor]))) > 0");
                    table.CheckConstraint("CK_LotesInsumo_Identificacao_NotBlank", "LEN(LTRIM(RTRIM([Identificacao]))) > 0");
                    table.ForeignKey(
                        name: "FK_LotesInsumo_InsumosEstoque_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "InsumosEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaldosInsumo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantidade_ValorBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Quantidade_UnidadeBase = table.Column<short>(type: "smallint", nullable: false),
                    Quantidade_Dimensao = table.Column<short>(type: "smallint", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaldosInsumo", x => x.Id);
                    table.CheckConstraint("CK_SaldosInsumo_Quantidade_Valid", "[Quantidade_ValorBase] >= 0 AND ([Quantidade_UnidadeBase] = 1 AND [Quantidade_Dimensao] = 1 OR [Quantidade_UnidadeBase] = 3 AND [Quantidade_Dimensao] = 2 OR [Quantidade_UnidadeBase] = 5 AND [Quantidade_Dimensao] = 3) AND ([Quantidade_UnidadeBase] <> 5 OR [Quantidade_ValorBase] = FLOOR([Quantidade_ValorBase]))");
                    table.ForeignKey(
                        name: "FK_SaldosInsumo_InsumosEstoque_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "InsumosEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaldosInsumo_LotesInsumo_InsumoId_LoteId",
                        columns: x => new { x.InsumoId, x.LoteId },
                        principalTable: "LotesInsumo",
                        principalColumns: new[] { "InsumoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LotesInsumo_InsumoId_DataValidade",
                table: "LotesInsumo",
                columns: new[] { "InsumoId", "DataValidade" });

            migrationBuilder.CreateIndex(
                name: "IX_LotesInsumo_InsumoId_Identificacao",
                table: "LotesInsumo",
                columns: new[] { "InsumoId", "Identificacao" });

            migrationBuilder.CreateIndex(
                name: "UX_SaldosInsumo_InsumoId_Agregado",
                table: "SaldosInsumo",
                column: "InsumoId",
                unique: true,
                filter: "[LoteId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SaldosInsumo_InsumoId_LoteId",
                table: "SaldosInsumo",
                columns: new[] { "InsumoId", "LoteId" },
                unique: true,
                filter: "[LoteId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaldosInsumo");

            migrationBuilder.DropTable(
                name: "LotesInsumo");
        }
    }
}
