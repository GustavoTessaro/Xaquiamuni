using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PersistirReservasProdutoAcabado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReservasProdutoAcabado",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ExpiraEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    Estado = table.Column<short>(type: "smallint", nullable: false),
                    ConfirmadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    EncerradaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    MotivoEncerramento = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservasProdutoAcabado", x => x.Id);
                    table.CheckConstraint("CK_ReservasProdutoAcabado_Cronologia_Valid", "([ConfirmadaEmUtc] IS NULL OR ([ConfirmadaEmUtc] >= [CriadaEmUtc] AND [ConfirmadaEmUtc] < [ExpiraEmUtc])) AND ([EncerradaEmUtc] IS NULL OR [EncerradaEmUtc] >= COALESCE([ConfirmadaEmUtc], [CriadaEmUtc]))");
                    table.CheckConstraint("CK_ReservasProdutoAcabado_Estado_Valid", "([Estado] = 0 AND [ConfirmadaEmUtc] IS NULL AND [EncerradaEmUtc] IS NULL) OR ([Estado] = 1 AND [ConfirmadaEmUtc] IS NOT NULL AND [EncerradaEmUtc] IS NULL) OR ([Estado] = 2 AND [EncerradaEmUtc] IS NOT NULL AND ([ConfirmadaEmUtc] IS NOT NULL OR [EncerradaEmUtc] < [ExpiraEmUtc])) OR ([Estado] = 3 AND [ConfirmadaEmUtc] IS NULL AND [EncerradaEmUtc] IS NOT NULL AND [EncerradaEmUtc] >= [ExpiraEmUtc]) OR ([Estado] = 4 AND [ConfirmadaEmUtc] IS NOT NULL AND [EncerradaEmUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ReservasProdutoAcabado_Identificadores_NotEmpty", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [PedidoId] <> '00000000-0000-0000-0000-000000000000' AND [ProdutoVarianteId] <> '00000000-0000-0000-0000-000000000000' AND [LoteId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ReservasProdutoAcabado_Motivo_Valid", "([Estado] = 2 AND [MotivoEncerramento] IS NOT NULL AND LEN(LTRIM(RTRIM([MotivoEncerramento]))) > 0) OR ([Estado] <> 2 AND [MotivoEncerramento] IS NULL)");
                    table.CheckConstraint("CK_ReservasProdutoAcabado_Prazo_24Horas", "DATEDIFF_BIG(hour, [CriadaEmUtc], [ExpiraEmUtc]) = 24 AND CAST([CriadaEmUtc] AS time(7)) = CAST([ExpiraEmUtc] AS time(7))");
                    table.CheckConstraint("CK_ReservasProdutoAcabado_Quantidade_Positive", "[Quantidade] > 0");
                    table.CheckConstraint("CK_ReservasProdutoAcabado_Timestamps_Utc", "DATEPART(TZOFFSET, [CriadaEmUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiraEmUtc]) = 0 AND ([ConfirmadaEmUtc] IS NULL OR DATEPART(TZOFFSET, [ConfirmadaEmUtc]) = 0) AND ([EncerradaEmUtc] IS NULL OR DATEPART(TZOFFSET, [EncerradaEmUtc]) = 0)");
                    table.ForeignKey(
                        name: "FK_ReservasProdutoAcabado_LotesProdutoAcabado_ProdutoVarianteId_LoteId",
                        columns: x => new { x.ProdutoVarianteId, x.LoteId },
                        principalTable: "LotesProdutoAcabado",
                        principalColumns: new[] { "ProdutoVarianteId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservasProdutoAcabado_Estado_ExpiraEmUtc",
                table: "ReservasProdutoAcabado",
                columns: new[] { "Estado", "ExpiraEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservasProdutoAcabado_PedidoId",
                table: "ReservasProdutoAcabado",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservasProdutoAcabado_ProdutoVarianteId_LoteId_Estado",
                table: "ReservasProdutoAcabado",
                columns: new[] { "ProdutoVarianteId", "LoteId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservasProdutoAcabado");
        }
    }
}
