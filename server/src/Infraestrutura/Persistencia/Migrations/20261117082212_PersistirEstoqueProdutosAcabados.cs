using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PersistirEstoqueProdutosAcabados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContagensSemDiferencaProdutoAcabado",
                columns: table => new
                {
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FingerprintRequisicao = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RegistradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContagensSemDiferencaProdutoAcabado", x => new { x.ResponsavelId, x.ChaveIdempotencia });
                    table.CheckConstraint("CK_ContagensSemDiferencaProdutoAcabado_Chave_NotBlank", "LEN(LTRIM(RTRIM([ChaveIdempotencia]))) > 0");
                    table.CheckConstraint("CK_ContagensSemDiferencaProdutoAcabado_Responsavel_NotEmpty", "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ContagensSemDiferencaProdutoAcabado_Timestamp_Utc", "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "LotesProdutoAcabado",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DataFabricacao = table.Column<DateOnly>(type: "date", nullable: false),
                    DataValidade = table.Column<DateOnly>(type: "date", nullable: false),
                    OrigemValidade = table.Column<short>(type: "smallint", nullable: false),
                    PrazoValidadeAplicadoDias = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesProdutoAcabado", x => x.Id);
                    table.UniqueConstraint("AK_LotesProdutoAcabado_ProdutoVarianteId_Id", x => new { x.ProdutoVarianteId, x.Id });
                    table.CheckConstraint("CK_LotesProdutoAcabado_Codigo_NotBlank", "LEN(LTRIM(RTRIM([Codigo]))) > 0");
                    table.CheckConstraint("CK_LotesProdutoAcabado_Validade_NotBeforeFabricacao", "[DataValidade] >= [DataFabricacao]");
                    table.CheckConstraint("CK_LotesProdutoAcabado_ValidadeOrigem_Valid", "([OrigemValidade] = 1 AND [PrazoValidadeAplicadoDias] IS NOT NULL AND [PrazoValidadeAplicadoDias] >= 0) OR ([OrigemValidade] = 2 AND [PrazoValidadeAplicadoDias] IS NULL)");
                    table.ForeignKey(
                        name: "FK_LotesProdutoAcabado_ProdutoVariantes_ProdutoVarianteId",
                        column: x => x.ProdutoVarianteId,
                        principalTable: "ProdutoVariantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PoliticasEstoqueProdutoVariante",
                columns: table => new
                {
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstoqueMinimo = table.Column<int>(type: "int", nullable: true),
                    PrazoValidadePadraoDias = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoliticasEstoqueProdutoVariante", x => x.ProdutoVarianteId);
                    table.CheckConstraint("CK_PoliticasEstoqueProdutoVariante_EstoqueMinimo_NonNegative", "[EstoqueMinimo] IS NULL OR [EstoqueMinimo] >= 0");
                    table.CheckConstraint("CK_PoliticasEstoqueProdutoVariante_PrazoValidade_NonNegative", "[PrazoValidadePadraoDias] IS NULL OR [PrazoValidadePadraoDias] >= 0");
                    table.ForeignKey(
                        name: "FK_PoliticasEstoqueProdutoVariante_ProdutoVariantes_ProdutoVarianteId",
                        column: x => x.ProdutoVarianteId,
                        principalTable: "ProdutoVariantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosProdutoAcabado",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<short>(type: "smallint", nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    CondicaoOrigem = table.Column<short>(type: "smallint", nullable: true),
                    CondicaoDestino = table.Column<short>(type: "smallint", nullable: true),
                    QuantidadeContada = table.Column<int>(type: "int", nullable: true),
                    SaldoAnterior_QuantidadeVendavel = table.Column<int>(type: "int", nullable: false),
                    SaldoAnterior_QuantidadeDanificada = table.Column<int>(type: "int", nullable: false),
                    SaldoAnterior_QuantidadeQuarentena = table.Column<int>(type: "int", nullable: false),
                    SaldoPosterior_QuantidadeVendavel = table.Column<int>(type: "int", nullable: false),
                    SaldoPosterior_QuantidadeDanificada = table.Column<int>(type: "int", nullable: false),
                    SaldoPosterior_QuantidadeQuarentena = table.Column<int>(type: "int", nullable: false),
                    MovimentoOriginalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RegistradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FingerprintRequisicao = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosProdutoAcabado", x => x.Id);
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Chave_NotBlank", "LEN(LTRIM(RTRIM([ChaveIdempotencia]))) > 0");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Condicoes_Valid", "([CondicaoOrigem] IS NULL OR [CondicaoOrigem] IN (1, 2, 3)) AND ([CondicaoDestino] IS NULL OR [CondicaoDestino] IN (1, 2, 3)) AND (([CondicaoOrigem] IS NULL AND [CondicaoDestino] IS NOT NULL) OR ([CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NULL) OR ([CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NOT NULL AND [CondicaoOrigem] <> [CondicaoDestino]))");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Contagem_Conciliada", "[Tipo] <> 6 OR ([CondicaoOrigem] IS NULL AND [CondicaoDestino] = 1 AND [QuantidadeContada] = [SaldoPosterior_QuantidadeVendavel] AND CONVERT(bigint, [Quantidade]) = ABS(CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeVendavel])) OR ([CondicaoOrigem] IS NULL AND [CondicaoDestino] = 2 AND [QuantidadeContada] = [SaldoPosterior_QuantidadeDanificada] AND CONVERT(bigint, [Quantidade]) = ABS(CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeDanificada])) OR ([CondicaoOrigem] IS NULL AND [CondicaoDestino] = 3 AND [QuantidadeContada] = [SaldoPosterior_QuantidadeQuarentena] AND CONVERT(bigint, [Quantidade]) = ABS(CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeQuarentena])) OR ([CondicaoDestino] IS NULL AND [CondicaoOrigem] = 1 AND [QuantidadeContada] = [SaldoPosterior_QuantidadeVendavel] AND CONVERT(bigint, [Quantidade]) = ABS(CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeVendavel])) OR ([CondicaoDestino] IS NULL AND [CondicaoOrigem] = 2 AND [QuantidadeContada] = [SaldoPosterior_QuantidadeDanificada] AND CONVERT(bigint, [Quantidade]) = ABS(CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeDanificada])) OR ([CondicaoDestino] IS NULL AND [CondicaoOrigem] = 3 AND [QuantidadeContada] = [SaldoPosterior_QuantidadeQuarentena] AND CONVERT(bigint, [Quantidade]) = ABS(CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeQuarentena]))");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Estorno_NotSelf", "[MovimentoOriginalId] IS NULL OR [MovimentoOriginalId] <> [Id]");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Motivo_NotBlank", "LEN(LTRIM(RTRIM([Motivo]))) > 0");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Quantidade_Positive", "[Quantidade] > 0");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Responsavel_NotEmpty", "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Saldos_NonNegativeAndIntRange", "[SaldoAnterior_QuantidadeVendavel] >= 0 AND [SaldoAnterior_QuantidadeDanificada] >= 0 AND [SaldoAnterior_QuantidadeQuarentena] >= 0 AND CONVERT(bigint, [SaldoAnterior_QuantidadeVendavel]) + CONVERT(bigint, [SaldoAnterior_QuantidadeDanificada]) + CONVERT(bigint, [SaldoAnterior_QuantidadeQuarentena]) <= 2147483647 AND [SaldoPosterior_QuantidadeVendavel] >= 0 AND [SaldoPosterior_QuantidadeDanificada] >= 0 AND [SaldoPosterior_QuantidadeQuarentena] >= 0 AND CONVERT(bigint, [SaldoPosterior_QuantidadeVendavel]) + CONVERT(bigint, [SaldoPosterior_QuantidadeDanificada]) + CONVERT(bigint, [SaldoPosterior_QuantidadeQuarentena]) <= 2147483647");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Saldos_TransitionValid", "CONVERT(bigint, [SaldoPosterior_QuantidadeVendavel]) = CASE WHEN [CondicaoOrigem] = 1 THEN CONVERT(bigint, [SaldoAnterior_QuantidadeVendavel]) - [Quantidade] WHEN [CondicaoDestino] = 1 THEN CONVERT(bigint, [SaldoAnterior_QuantidadeVendavel]) + [Quantidade] ELSE [SaldoAnterior_QuantidadeVendavel] END AND CONVERT(bigint, [SaldoPosterior_QuantidadeDanificada]) = CASE WHEN [CondicaoOrigem] = 2 THEN CONVERT(bigint, [SaldoAnterior_QuantidadeDanificada]) - [Quantidade] WHEN [CondicaoDestino] = 2 THEN CONVERT(bigint, [SaldoAnterior_QuantidadeDanificada]) + [Quantidade] ELSE [SaldoAnterior_QuantidadeDanificada] END AND CONVERT(bigint, [SaldoPosterior_QuantidadeQuarentena]) = CASE WHEN [CondicaoOrigem] = 3 THEN CONVERT(bigint, [SaldoAnterior_QuantidadeQuarentena]) - [Quantidade] WHEN [CondicaoDestino] = 3 THEN CONVERT(bigint, [SaldoAnterior_QuantidadeQuarentena]) + [Quantidade] ELSE [SaldoAnterior_QuantidadeQuarentena] END");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Timestamps_Utc", "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Tipo_DadosValid", "(([Tipo] IN (1, 2) AND [CondicaoOrigem] IS NULL AND [CondicaoDestino] IS NOT NULL AND [QuantidadeContada] IS NULL AND [MovimentoOriginalId] IS NULL) OR ([Tipo] IN (3, 4, 5) AND [CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NULL AND [QuantidadeContada] IS NULL AND [MovimentoOriginalId] IS NULL) OR ([Tipo] = 6 AND (([CondicaoOrigem] IS NULL AND [CondicaoDestino] IS NOT NULL) OR ([CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NULL)) AND [QuantidadeContada] >= 0 AND [MovimentoOriginalId] IS NULL) OR ([Tipo] = 7 AND [CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NOT NULL AND [QuantidadeContada] IS NULL AND [MovimentoOriginalId] IS NULL) OR ([Tipo] = 8 AND [MovimentoOriginalId] IS NOT NULL AND [QuantidadeContada] IS NULL))");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Contagem_NotNull", "[Tipo] <> 6 OR [QuantidadeContada] IS NOT NULL");
                    table.CheckConstraint("CK_MovimentosProdutoAcabado_Tipo_Valid", "[Tipo] IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.ForeignKey(
                        name: "FK_MovimentosProdutoAcabado_LotesProdutoAcabado_ProdutoVarianteId_LoteId",
                        columns: x => new { x.ProdutoVarianteId, x.LoteId },
                        principalTable: "LotesProdutoAcabado",
                        principalColumns: new[] { "ProdutoVarianteId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosProdutoAcabado_MovimentosProdutoAcabado_MovimentoOriginalId",
                        column: x => x.MovimentoOriginalId,
                        principalTable: "MovimentosProdutoAcabado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaldosProdutoAcabado",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantidadeVendavel = table.Column<int>(type: "int", nullable: false),
                    QuantidadeDanificada = table.Column<int>(type: "int", nullable: false),
                    QuantidadeQuarentena = table.Column<int>(type: "int", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaldosProdutoAcabado", x => x.Id);
                    table.CheckConstraint("CK_SaldosProdutoAcabado_QuantidadeFisica_IntRange", "CONVERT(bigint, [QuantidadeVendavel]) + CONVERT(bigint, [QuantidadeDanificada]) + CONVERT(bigint, [QuantidadeQuarentena]) <= 2147483647");
                    table.CheckConstraint("CK_SaldosProdutoAcabado_Quantidades_NonNegative", "[QuantidadeVendavel] >= 0 AND [QuantidadeDanificada] >= 0 AND [QuantidadeQuarentena] >= 0");
                    table.ForeignKey(
                        name: "FK_SaldosProdutoAcabado_LotesProdutoAcabado_ProdutoVarianteId_LoteId",
                        columns: x => new { x.ProdutoVarianteId, x.LoteId },
                        principalTable: "LotesProdutoAcabado",
                        principalColumns: new[] { "ProdutoVarianteId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LotesProdutoAcabado_ProdutoVarianteId_DataValidade",
                table: "LotesProdutoAcabado",
                columns: new[] { "ProdutoVarianteId", "DataValidade" });

            migrationBuilder.CreateIndex(
                name: "UX_LotesProdutoAcabado_Codigo",
                table: "LotesProdutoAcabado",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosProdutoAcabado_ProdutoVarianteId_LoteId_RegistradoEmUtc",
                table: "MovimentosProdutoAcabado",
                columns: new[] { "ProdutoVarianteId", "LoteId", "RegistradoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosProdutoAcabado_ProdutoVarianteId_RegistradoEmUtc",
                table: "MovimentosProdutoAcabado",
                columns: new[] { "ProdutoVarianteId", "RegistradoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_MovimentosProdutoAcabado_MovimentoOriginalId",
                table: "MovimentosProdutoAcabado",
                column: "MovimentoOriginalId",
                unique: true,
                filter: "[MovimentoOriginalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_MovimentosProdutoAcabado_ResponsavelId_ChaveIdempotencia",
                table: "MovimentosProdutoAcabado",
                columns: new[] { "ResponsavelId", "ChaveIdempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SaldosProdutoAcabado_ProdutoVarianteId_LoteId",
                table: "SaldosProdutoAcabado",
                columns: new[] { "ProdutoVarianteId", "LoteId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_MovimentosProdutoAcabado_Estorno_Valid]
                ON [dbo].[MovimentosProdutoAcabado]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS
                    (
                        SELECT 1
                        FROM inserted AS [novo]
                        LEFT JOIN [dbo].[MovimentosProdutoAcabado] AS [original]
                            ON [original].[Id] = [novo].[MovimentoOriginalId]
                        WHERE [novo].[Tipo] = 8
                          AND
                          (
                              [original].[Id] IS NULL
                              OR [original].[Tipo] = 8
                              OR [original].[ProdutoVarianteId] <> [novo].[ProdutoVarianteId]
                              OR [original].[LoteId] <> [novo].[LoteId]
                              OR [original].[Quantidade] <> [novo].[Quantidade]
                              OR ISNULL([original].[CondicaoDestino], 0)
                                 <> ISNULL([novo].[CondicaoOrigem], 0)
                              OR ISNULL([original].[CondicaoOrigem], 0)
                                 <> ISNULL([novo].[CondicaoDestino], 0)
                          )
                    )
                        THROW 51007, 'O estorno deve compensar um movimento da mesma variante e lote.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_MovimentosProdutoAcabado_AppendOnly]
                ON [dbo].[MovimentosProdutoAcabado]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51008, 'Movimentos de produto acabado são imutáveis.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_ContagensSemDiferencaProdutoAcabado_AppendOnly]
                ON [dbo].[ContagensSemDiferencaProdutoAcabado]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51009, 'Registros de idempotência são imutáveis.', 1;
                END;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_MovimentosProdutoAcabado_Estorno_Valid];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_MovimentosProdutoAcabado_AppendOnly];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_ContagensSemDiferencaProdutoAcabado_AppendOnly];");

            migrationBuilder.DropTable(
                name: "ContagensSemDiferencaProdutoAcabado");

            migrationBuilder.DropTable(
                name: "MovimentosProdutoAcabado");

            migrationBuilder.DropTable(
                name: "PoliticasEstoqueProdutoVariante");

            migrationBuilder.DropTable(
                name: "SaldosProdutoAcabado");

            migrationBuilder.DropTable(
                name: "LotesProdutoAcabado");
        }
    }
}
