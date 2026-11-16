using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PersistirInsumosEFormasCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsumosEstoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NomeNormalizado = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Tipo = table.Column<short>(type: "smallint", nullable: false),
                    UnidadeBase = table.Column<short>(type: "smallint", nullable: false),
                    EstoqueMinimo_ValorBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    EstoqueMinimo_UnidadeBase = table.Column<short>(type: "smallint", nullable: true),
                    EstoqueMinimo_Dimensao = table.Column<short>(type: "smallint", nullable: true),
                    ControlaLote = table.Column<bool>(type: "bit", nullable: false),
                    ValidadeObrigatoria = table.Column<bool>(type: "bit", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsumosEstoque", x => x.Id);
                    table.CheckConstraint("CK_InsumosEstoque_EstoqueMinimo_Valid", "([EstoqueMinimo_ValorBase] IS NULL AND [EstoqueMinimo_UnidadeBase] IS NULL AND [EstoqueMinimo_Dimensao] IS NULL) OR ([EstoqueMinimo_ValorBase] IS NOT NULL AND [EstoqueMinimo_UnidadeBase] IS NOT NULL AND [EstoqueMinimo_Dimensao] IS NOT NULL AND [EstoqueMinimo_UnidadeBase] = [UnidadeBase] AND ([EstoqueMinimo_UnidadeBase] = 1 AND [EstoqueMinimo_Dimensao] = 1 OR [EstoqueMinimo_UnidadeBase] = 3 AND [EstoqueMinimo_Dimensao] = 2 OR [EstoqueMinimo_UnidadeBase] = 5 AND [EstoqueMinimo_Dimensao] = 3) AND [EstoqueMinimo_ValorBase] >= 0 AND ([UnidadeBase] <> 5 OR [EstoqueMinimo_ValorBase] = FLOOR([EstoqueMinimo_ValorBase])))");
                    table.CheckConstraint("CK_InsumosEstoque_Nome_NotBlank", "LEN(LTRIM(RTRIM([Nome]))) > 0 AND LEN(LTRIM(RTRIM([NomeNormalizado]))) > 0");
                    table.CheckConstraint("CK_InsumosEstoque_Tipo_Valid", "[Tipo] IN (1, 2, 3)");
                    table.CheckConstraint("CK_InsumosEstoque_UnidadeBase_Valid", "[UnidadeBase] IN (1, 3, 5)");
                    table.CheckConstraint("CK_InsumosEstoque_ValidadeRequerLote", "[ValidadeObrigatoria] = 0 OR [ControlaLote] = 1");
                });

            migrationBuilder.CreateTable(
                name: "FormasCompraInsumo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NomeNormalizado = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Fator_ValorBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Fator_UnidadeBase = table.Column<short>(type: "smallint", nullable: false),
                    Fator_Dimensao = table.Column<short>(type: "smallint", nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormasCompraInsumo", x => x.Id);
                    table.CheckConstraint("CK_FormasCompraInsumo_Fator_Valid", "[Fator_ValorBase] > 0 AND ([Fator_UnidadeBase] = 1 AND [Fator_Dimensao] = 1 OR [Fator_UnidadeBase] = 3 AND [Fator_Dimensao] = 2 OR [Fator_UnidadeBase] = 5 AND [Fator_Dimensao] = 3) AND ([Fator_UnidadeBase] <> 5 OR [Fator_ValorBase] = FLOOR([Fator_ValorBase]))");
                    table.CheckConstraint("CK_FormasCompraInsumo_Nome_NotBlank", "LEN(LTRIM(RTRIM([Nome]))) > 0 AND LEN(LTRIM(RTRIM([NomeNormalizado]))) > 0");
                    table.ForeignKey(
                        name: "FK_FormasCompraInsumo_InsumosEstoque_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "InsumosEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_FormasCompraInsumo_InsumoId_NomeNormalizado",
                table: "FormasCompraInsumo",
                columns: new[] { "InsumoId", "NomeNormalizado" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormasCompraInsumo");

            migrationBuilder.DropTable(
                name: "InsumosEstoque");
        }
    }
}
