using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarEntradasInsumos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MovimentosInsumo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<short>(type: "smallint", nullable: false),
                    QuantidadeOriginal = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnidadeOriginal = table.Column<short>(type: "smallint", nullable: true),
                    FormaCompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FormaCompraNome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FatorCompra_ValorBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    FatorCompra_UnidadeBase = table.Column<short>(type: "smallint", nullable: true),
                    FatorCompra_Dimensao = table.Column<short>(type: "smallint", nullable: true),
                    QuantidadeBase_ValorBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    QuantidadeBase_UnidadeBase = table.Column<short>(type: "smallint", nullable: false),
                    QuantidadeBase_Dimensao = table.Column<short>(type: "smallint", nullable: false),
                    SaldoAnterior_ValorBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SaldoAnterior_UnidadeBase = table.Column<short>(type: "smallint", nullable: false),
                    SaldoAnterior_Dimensao = table.Column<short>(type: "smallint", nullable: false),
                    SaldoPosterior_ValorBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SaldoPosterior_UnidadeBase = table.Column<short>(type: "smallint", nullable: false),
                    SaldoPosterior_Dimensao = table.Column<short>(type: "smallint", nullable: false),
                    CustoTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CustoPorUnidadeBase = table.Column<decimal>(type: "decimal(38,14)", precision: 38, scale: 14, nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RegistradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FingerprintRequisicao = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosInsumo", x => x.Id);
                    table.CheckConstraint("CK_MovimentosInsumo_Custo_Valid", "([CustoTotal] IS NULL AND [CustoPorUnidadeBase] IS NULL) OR ([CustoTotal] IS NOT NULL AND [CustoPorUnidadeBase] IS NOT NULL AND [CustoTotal] >= 0 AND [CustoPorUnidadeBase] >= 0)");
                    table.CheckConstraint("CK_MovimentosInsumo_Entrada_Valid", "[QuantidadeOriginal] > 0 AND [QuantidadeBase_ValorBase] > 0 AND [SaldoAnterior_ValorBase] >= 0 AND [SaldoPosterior_ValorBase] = [SaldoAnterior_ValorBase] + [QuantidadeBase_ValorBase] AND [SaldoAnterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND [SaldoPosterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND [SaldoAnterior_Dimensao] = [QuantidadeBase_Dimensao] AND [SaldoPosterior_Dimensao] = [QuantidadeBase_Dimensao]");
                    table.CheckConstraint("CK_MovimentosInsumo_Origem_Valid", "(([FormaCompraId] IS NULL AND [FormaCompraNome] IS NULL AND [FatorCompra_ValorBase] IS NULL AND [FatorCompra_UnidadeBase] IS NULL AND [FatorCompra_Dimensao] IS NULL AND [UnidadeOriginal] IS NOT NULL AND [QuantidadeOriginal] > 0) OR ([FormaCompraId] IS NOT NULL AND [FormaCompraNome] IS NOT NULL AND LEN(LTRIM(RTRIM([FormaCompraNome]))) > 0 AND [FatorCompra_ValorBase] > 0 AND [FatorCompra_UnidadeBase] IS NOT NULL AND [FatorCompra_Dimensao] IS NOT NULL AND [UnidadeOriginal] IS NULL AND [QuantidadeOriginal] = FLOOR([QuantidadeOriginal]) AND [FatorCompra_Dimensao] = [QuantidadeBase_Dimensao] AND (([FatorCompra_UnidadeBase] = 1 AND [FatorCompra_Dimensao] = 1) OR ([FatorCompra_UnidadeBase] = 3 AND [FatorCompra_Dimensao] = 2) OR ([FatorCompra_UnidadeBase] = 5 AND [FatorCompra_Dimensao] = 3)))) AND (([UnidadeOriginal] IS NULL) OR ([UnidadeOriginal] = 1 AND [QuantidadeBase_UnidadeBase] = 1) OR ([UnidadeOriginal] = 2 AND [QuantidadeBase_UnidadeBase] = 1 OR [UnidadeOriginal] IN (3, 4) AND [QuantidadeBase_UnidadeBase] = 3 OR [UnidadeOriginal] = 5 AND [QuantidadeBase_UnidadeBase] = 5))");
                    table.CheckConstraint("CK_MovimentosInsumo_Responsavel_NotEmpty", "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_MovimentosInsumo_Timestamps_Utc", "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
                    table.CheckConstraint("CK_MovimentosInsumo_Tipo_Valid", "[Tipo] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_MovimentosInsumo_InsumosEstoque_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "InsumosEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosInsumo_LotesInsumo_InsumoId_LoteId",
                        columns: x => new { x.InsumoId, x.LoteId },
                        principalTable: "LotesInsumo",
                        principalColumns: new[] { "InsumoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosInsumo_InsumoId_LoteId_RegistradoEmUtc",
                table: "MovimentosInsumo",
                columns: new[] { "InsumoId", "LoteId", "RegistradoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_MovimentosInsumo_ResponsavelId_ChaveIdempotencia",
                table: "MovimentosInsumo",
                columns: new[] { "ResponsavelId", "ChaveIdempotencia" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_MovimentosInsumo_AppendOnly]
                ON [dbo].[MovimentosInsumo]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51002, 'Movimentos de insumo são append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_MovimentosInsumo_AppendOnly];");

            migrationBuilder.DropTable(
                name: "MovimentosInsumo");
        }
    }
}
