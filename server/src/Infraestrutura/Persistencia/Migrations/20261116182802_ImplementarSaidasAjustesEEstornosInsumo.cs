using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ImplementarSaidasAjustesEEstornosInsumo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Custo_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Entrada_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Origem_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Tipo_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.AddColumn<short>(
                name: "Direcao",
                table: "MovimentosInsumo",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<Guid>(
                name: "MovimentoOriginalId",
                table: "MovimentosInsumo",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "QuantidadeContada_Dimensao",
                table: "MovimentosInsumo",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "QuantidadeContada_UnidadeBase",
                table: "MovimentosInsumo",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantidadeContada_ValorBase",
                table: "MovimentosInsumo",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AjustesSemDiferencaInsumo",
                columns: table => new
                {
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FingerprintRequisicao = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RegistradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AjustesSemDiferencaInsumo", x => new { x.ResponsavelId, x.ChaveIdempotencia });
                    table.CheckConstraint("CK_AjustesSemDiferencaInsumo_Chave_NotBlank", "LEN(LTRIM(RTRIM([ChaveIdempotencia]))) > 0");
                    table.CheckConstraint("CK_AjustesSemDiferencaInsumo_Responsavel_NotEmpty", "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AjustesSemDiferencaInsumo_Timestamp_Utc", "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
                });

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_AjustesSemDiferencaInsumo_AppendOnly]
                ON [dbo].[AjustesSemDiferencaInsumo]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51005, 'Registros de idempotência de ajuste são imutáveis.', 1;
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_MovimentosInsumo_MovimentoOriginalId",
                table: "MovimentosInsumo",
                column: "MovimentoOriginalId",
                unique: true,
                filter: "[MovimentoOriginalId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Contagem_Conciliada",
                table: "MovimentosInsumo",
                sql: "[Tipo] <> 6 OR ([QuantidadeContada_ValorBase] = [SaldoPosterior_ValorBase] AND [QuantidadeBase_ValorBase] = ABS([QuantidadeContada_ValorBase] - [SaldoAnterior_ValorBase]))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Contagem_Valid",
                table: "MovimentosInsumo",
                sql: "(([Tipo] = 6 AND [QuantidadeContada_ValorBase] IS NOT NULL AND [QuantidadeContada_UnidadeBase] IS NOT NULL AND [QuantidadeContada_Dimensao] IS NOT NULL AND [QuantidadeContada_ValorBase] >= 0 AND [QuantidadeContada_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND [QuantidadeContada_Dimensao] = [QuantidadeBase_Dimensao] AND (([UnidadeOriginal] = 1 AND [QuantidadeOriginal] = [QuantidadeContada_ValorBase]) OR ([UnidadeOriginal] = 2 AND [QuantidadeOriginal] * 1000 = [QuantidadeContada_ValorBase]) OR ([UnidadeOriginal] = 3 AND [QuantidadeOriginal] = [QuantidadeContada_ValorBase]) OR ([UnidadeOriginal] = 4 AND [QuantidadeOriginal] * 1000 = [QuantidadeContada_ValorBase]) OR ([UnidadeOriginal] = 5 AND [QuantidadeOriginal] = [QuantidadeContada_ValorBase]))) OR ([Tipo] <> 6 AND [QuantidadeContada_ValorBase] IS NULL AND [QuantidadeContada_UnidadeBase] IS NULL AND [QuantidadeContada_Dimensao] IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Custo_Valid",
                table: "MovimentosInsumo",
                sql: "([CustoTotal] IS NULL AND [CustoPorUnidadeBase] IS NULL) OR ([Tipo] IN (1, 2) AND [CustoTotal] IS NOT NULL AND [CustoPorUnidadeBase] IS NOT NULL AND [CustoTotal] >= 0 AND [CustoPorUnidadeBase] >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Direcao_Saldo_Valid",
                table: "MovimentosInsumo",
                sql: "[Direcao] IN (-1, 1) AND [QuantidadeBase_ValorBase] > 0 AND [SaldoAnterior_ValorBase] >= 0 AND (([Direcao] = 1 AND [SaldoPosterior_ValorBase] = [SaldoAnterior_ValorBase] + [QuantidadeBase_ValorBase]) OR ([Direcao] = -1 AND [SaldoPosterior_ValorBase] = [SaldoAnterior_ValorBase] - [QuantidadeBase_ValorBase] AND [SaldoPosterior_ValorBase] >= 0)) AND [SaldoAnterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND [SaldoPosterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND [SaldoAnterior_Dimensao] = [QuantidadeBase_Dimensao] AND [SaldoPosterior_Dimensao] = [QuantidadeBase_Dimensao]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Estorno_Valid",
                table: "MovimentosInsumo",
                sql: "([Tipo] = 7 AND [MovimentoOriginalId] IS NOT NULL AND [MovimentoOriginalId] <> [Id] AND [CustoTotal] IS NULL AND [CustoPorUnidadeBase] IS NULL) OR ([Tipo] <> 7 AND [MovimentoOriginalId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Origem_Valid",
                table: "MovimentosInsumo",
                sql: "(([Tipo] IN (1, 2) AND (([FormaCompraId] IS NULL AND [FormaCompraNome] IS NULL AND [FatorCompra_ValorBase] IS NULL AND [FatorCompra_UnidadeBase] IS NULL AND [FatorCompra_Dimensao] IS NULL AND [UnidadeOriginal] IS NOT NULL AND [QuantidadeOriginal] > 0) OR ([FormaCompraId] IS NOT NULL AND [FormaCompraNome] IS NOT NULL AND LEN(LTRIM(RTRIM([FormaCompraNome]))) > 0 AND [FatorCompra_ValorBase] > 0 AND [FatorCompra_UnidadeBase] IS NOT NULL AND [FatorCompra_Dimensao] IS NOT NULL AND [UnidadeOriginal] IS NULL AND [QuantidadeOriginal] = FLOOR([QuantidadeOriginal]) AND [FatorCompra_Dimensao] = [QuantidadeBase_Dimensao] AND (([FatorCompra_UnidadeBase] = 1 AND [FatorCompra_Dimensao] = 1) OR ([FatorCompra_UnidadeBase] = 3 AND [FatorCompra_Dimensao] = 2) OR ([FatorCompra_UnidadeBase] = 5 AND [FatorCompra_Dimensao] = 3))))) OR ([Tipo] IN (3, 4, 5, 6, 7) AND [FormaCompraId] IS NULL AND [FormaCompraNome] IS NULL AND [FatorCompra_ValorBase] IS NULL AND [FatorCompra_UnidadeBase] IS NULL AND [FatorCompra_Dimensao] IS NULL AND [UnidadeOriginal] IS NOT NULL AND (([Tipo] = 6 AND [QuantidadeOriginal] >= 0) OR ([Tipo] <> 6 AND [QuantidadeOriginal] > 0)))) AND (([UnidadeOriginal] IS NULL) OR ([UnidadeOriginal] = 1 AND [QuantidadeBase_UnidadeBase] = 1) OR ([UnidadeOriginal] = 2 AND [QuantidadeBase_UnidadeBase] = 1 OR [UnidadeOriginal] IN (3, 4) AND [QuantidadeBase_UnidadeBase] = 3 OR [UnidadeOriginal] = 5 AND [QuantidadeBase_UnidadeBase] = 5))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Tipo_Direcao_Valid",
                table: "MovimentosInsumo",
                sql: "([Tipo] IN (1, 2) AND [Direcao] = 1) OR ([Tipo] IN (3, 4, 5) AND [Direcao] = -1) OR ([Tipo] IN (6, 7) AND [Direcao] IN (-1, 1))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Tipo_Valid",
                table: "MovimentosInsumo",
                sql: "[Tipo] IN (1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosInsumo_MovimentosInsumo_MovimentoOriginalId",
                table: "MovimentosInsumo",
                column: "MovimentoOriginalId",
                principalTable: "MovimentosInsumo",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_MovimentosInsumo_Estorno_Valid]
                ON [dbo].[MovimentosInsumo]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS
                    (
                        SELECT 1
                        FROM inserted AS [novo]
                        LEFT JOIN [dbo].[MovimentosInsumo] AS [original]
                            ON [original].[Id] = [novo].[MovimentoOriginalId]
                        WHERE [novo].[Tipo] = 7
                          AND
                          (
                              [original].[Id] IS NULL
                              OR [original].[Tipo] = 7
                              OR [original].[InsumoId] <> [novo].[InsumoId]
                              OR ISNULL([original].[LoteId], '00000000-0000-0000-0000-000000000000')
                                 <> ISNULL([novo].[LoteId], '00000000-0000-0000-0000-000000000000')
                              OR [original].[Direcao] = [novo].[Direcao]
                              OR [original].[QuantidadeBase_ValorBase] <> [novo].[QuantidadeBase_ValorBase]
                              OR [original].[QuantidadeBase_UnidadeBase] <> [novo].[QuantidadeBase_UnidadeBase]
                              OR [original].[QuantidadeBase_Dimensao] <> [novo].[QuantidadeBase_Dimensao]
                          )
                    )
                        THROW 51006, 'O estorno deve compensar uma movimentação original da mesma posição.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [dbo].[MovimentosInsumo] WHERE [Tipo] NOT IN (1, 2))
                    THROW 51003, 'F4.2.4 movements cannot be removed by migration rollback.', 1;
                IF EXISTS (SELECT 1 FROM [dbo].[AjustesSemDiferencaInsumo])
                    THROW 51004, 'Idempotent no-difference adjustment records cannot be removed by migration rollback.', 1;
                """);

            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_MovimentosInsumo_Estorno_Valid];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_AjustesSemDiferencaInsumo_AppendOnly];");

            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosInsumo_MovimentosInsumo_MovimentoOriginalId",
                table: "MovimentosInsumo");

            migrationBuilder.DropTable(
                name: "AjustesSemDiferencaInsumo");

            migrationBuilder.DropIndex(
                name: "UX_MovimentosInsumo_MovimentoOriginalId",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Contagem_Conciliada",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Contagem_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Custo_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Direcao_Saldo_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Estorno_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Origem_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Tipo_Direcao_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovimentosInsumo_Tipo_Valid",
                table: "MovimentosInsumo");

            migrationBuilder.DropColumn(
                name: "Direcao",
                table: "MovimentosInsumo");

            migrationBuilder.DropColumn(
                name: "MovimentoOriginalId",
                table: "MovimentosInsumo");

            migrationBuilder.DropColumn(
                name: "QuantidadeContada_Dimensao",
                table: "MovimentosInsumo");

            migrationBuilder.DropColumn(
                name: "QuantidadeContada_UnidadeBase",
                table: "MovimentosInsumo");

            migrationBuilder.DropColumn(
                name: "QuantidadeContada_ValorBase",
                table: "MovimentosInsumo");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Custo_Valid",
                table: "MovimentosInsumo",
                sql: "([CustoTotal] IS NULL AND [CustoPorUnidadeBase] IS NULL) OR ([CustoTotal] IS NOT NULL AND [CustoPorUnidadeBase] IS NOT NULL AND [CustoTotal] >= 0 AND [CustoPorUnidadeBase] >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Entrada_Valid",
                table: "MovimentosInsumo",
                sql: "[QuantidadeOriginal] > 0 AND [QuantidadeBase_ValorBase] > 0 AND [SaldoAnterior_ValorBase] >= 0 AND [SaldoPosterior_ValorBase] = [SaldoAnterior_ValorBase] + [QuantidadeBase_ValorBase] AND [SaldoAnterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND [SaldoPosterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND [SaldoAnterior_Dimensao] = [QuantidadeBase_Dimensao] AND [SaldoPosterior_Dimensao] = [QuantidadeBase_Dimensao]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Origem_Valid",
                table: "MovimentosInsumo",
                sql: "(([FormaCompraId] IS NULL AND [FormaCompraNome] IS NULL AND [FatorCompra_ValorBase] IS NULL AND [FatorCompra_UnidadeBase] IS NULL AND [FatorCompra_Dimensao] IS NULL AND [UnidadeOriginal] IS NOT NULL AND [QuantidadeOriginal] > 0) OR ([FormaCompraId] IS NOT NULL AND [FormaCompraNome] IS NOT NULL AND LEN(LTRIM(RTRIM([FormaCompraNome]))) > 0 AND [FatorCompra_ValorBase] > 0 AND [FatorCompra_UnidadeBase] IS NOT NULL AND [FatorCompra_Dimensao] IS NOT NULL AND [UnidadeOriginal] IS NULL AND [QuantidadeOriginal] = FLOOR([QuantidadeOriginal]) AND [FatorCompra_Dimensao] = [QuantidadeBase_Dimensao] AND (([FatorCompra_UnidadeBase] = 1 AND [FatorCompra_Dimensao] = 1) OR ([FatorCompra_UnidadeBase] = 3 AND [FatorCompra_Dimensao] = 2) OR ([FatorCompra_UnidadeBase] = 5 AND [FatorCompra_Dimensao] = 3)))) AND (([UnidadeOriginal] IS NULL) OR ([UnidadeOriginal] = 1 AND [QuantidadeBase_UnidadeBase] = 1) OR ([UnidadeOriginal] = 2 AND [QuantidadeBase_UnidadeBase] = 1 OR [UnidadeOriginal] IN (3, 4) AND [QuantidadeBase_UnidadeBase] = 3 OR [UnidadeOriginal] = 5 AND [QuantidadeBase_UnidadeBase] = 5))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovimentosInsumo_Tipo_Valid",
                table: "MovimentosInsumo",
                sql: "[Tipo] IN (1, 2)");
        }
    }
}
