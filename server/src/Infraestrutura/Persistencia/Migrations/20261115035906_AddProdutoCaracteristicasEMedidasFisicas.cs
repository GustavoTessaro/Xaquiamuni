using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddProdutoCaracteristicasEMedidasFisicas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CombinacaoHash",
                table: "ProdutoVariantes",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PesoLiquidoGramas",
                table: "ProdutoVariantes",
                type: "decimal(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeLiquidoMililitros",
                table: "ProdutoVariantes",
                type: "decimal(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProdutoVariantes_ProdutoId_Id",
                table: "ProdutoVariantes",
                columns: new[] { "ProdutoId", "Id" });

            migrationBuilder.CreateTable(
                name: "ProdutoCaracteristicas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NomeNormalizado = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TipoMedidaFisica = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdutoCaracteristicas", x => x.Id);
                    table.UniqueConstraint("AK_ProdutoCaracteristicas_ProdutoId_Id", x => new { x.ProdutoId, x.Id });
                    table.CheckConstraint("CK_ProdutoCaracteristicas_Nome_NotBlank", "LEN(LTRIM(RTRIM([Nome]))) > 0");
                    table.CheckConstraint("CK_ProdutoCaracteristicas_TipoMedidaFisica_Valid", "[TipoMedidaFisica] IS NULL OR [TipoMedidaFisica] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ProdutoCaracteristicas_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProdutoCaracteristicaValores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoCaracteristicaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NomeNormalizado = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValorMedidaCanonico = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdutoCaracteristicaValores", x => x.Id);
                    table.UniqueConstraint("AK_ProdutoCaracteristicaValores_Produto_Caracteristica_Id", x => new { x.ProdutoId, x.ProdutoCaracteristicaId, x.Id });
                    table.CheckConstraint("CK_ProdutoCaracteristicaValores_ValorMedidaCanonico_Positive", "[ValorMedidaCanonico] IS NULL OR [ValorMedidaCanonico] > 0");
                    table.ForeignKey(
                        name: "FK_ProdutoCaracteristicaValores_ProdutoCaracteristicas_ProdutoId_ProdutoCaracteristicaId",
                        columns: x => new { x.ProdutoId, x.ProdutoCaracteristicaId },
                        principalTable: "ProdutoCaracteristicas",
                        principalColumns: new[] { "ProdutoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProdutoVarianteCaracteristicaValores",
                columns: table => new
                {
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoCaracteristicaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoCaracteristicaValorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdutoVarianteCaracteristicaValores", x => new { x.ProdutoVarianteId, x.ProdutoCaracteristicaId });
                    table.ForeignKey(
                        name: "FK_ProdutoVarianteCaracteristicaValores_ProdutoCaracteristicaValores_ProdutoId_ProdutoCaracteristicaId_ProdutoCaracteristicaVal~",
                        columns: x => new { x.ProdutoId, x.ProdutoCaracteristicaId, x.ProdutoCaracteristicaValorId },
                        principalTable: "ProdutoCaracteristicaValores",
                        principalColumns: new[] { "ProdutoId", "ProdutoCaracteristicaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProdutoVarianteCaracteristicaValores_ProdutoCaracteristicas_ProdutoId_ProdutoCaracteristicaId",
                        columns: x => new { x.ProdutoId, x.ProdutoCaracteristicaId },
                        principalTable: "ProdutoCaracteristicas",
                        principalColumns: new[] { "ProdutoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProdutoVarianteCaracteristicaValores_ProdutoVariantes_ProdutoId_ProdutoVarianteId",
                        columns: x => new { x.ProdutoId, x.ProdutoVarianteId },
                        principalTable: "ProdutoVariantes",
                        principalColumns: new[] { "ProdutoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoVariantes_ProdutoId_CombinacaoHash",
                table: "ProdutoVariantes",
                columns: new[] { "ProdutoId", "CombinacaoHash" },
                unique: true,
                filter: "[CombinacaoHash] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProdutoVariantes_PesoLiquidoGramas_Positive",
                table: "ProdutoVariantes",
                sql: "[PesoLiquidoGramas] IS NULL OR [PesoLiquidoGramas] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProdutoVariantes_VolumeLiquidoMililitros_Positive",
                table: "ProdutoVariantes",
                sql: "[VolumeLiquidoMililitros] IS NULL OR [VolumeLiquidoMililitros] > 0");

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoCaracteristicas_ProdutoId_NomeNormalizado",
                table: "ProdutoCaracteristicas",
                columns: new[] { "ProdutoId", "NomeNormalizado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoCaracteristicas_ProdutoId_TipoMedidaFisica",
                table: "ProdutoCaracteristicas",
                columns: new[] { "ProdutoId", "TipoMedidaFisica" },
                unique: true,
                filter: "[TipoMedidaFisica] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoCaracteristicaValores_Produto_Caracteristica_NomeNormalizado",
                table: "ProdutoCaracteristicaValores",
                columns: new[] { "ProdutoId", "ProdutoCaracteristicaId", "NomeNormalizado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProdutoVarianteCaracteristicaValores_Produto_Variante",
                table: "ProdutoVarianteCaracteristicaValores",
                columns: new[] { "ProdutoId", "ProdutoVarianteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProdutoVarianteCaracteristicaValores_ProdutoId_ProdutoCaracteristicaId_ProdutoCaracteristicaValorId",
                table: "ProdutoVarianteCaracteristicaValores",
                columns: new[] { "ProdutoId", "ProdutoCaracteristicaId", "ProdutoCaracteristicaValorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProdutoVarianteCaracteristicaValores");

            migrationBuilder.DropTable(
                name: "ProdutoCaracteristicaValores");

            migrationBuilder.DropTable(
                name: "ProdutoCaracteristicas");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProdutoVariantes_ProdutoId_Id",
                table: "ProdutoVariantes");

            migrationBuilder.DropIndex(
                name: "UX_ProdutoVariantes_ProdutoId_CombinacaoHash",
                table: "ProdutoVariantes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProdutoVariantes_PesoLiquidoGramas_Positive",
                table: "ProdutoVariantes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProdutoVariantes_VolumeLiquidoMililitros_Positive",
                table: "ProdutoVariantes");

            migrationBuilder.DropColumn(
                name: "CombinacaoHash",
                table: "ProdutoVariantes");

            migrationBuilder.DropColumn(
                name: "PesoLiquidoGramas",
                table: "ProdutoVariantes");

            migrationBuilder.DropColumn(
                name: "VolumeLiquidoMililitros",
                table: "ProdutoVariantes");
        }
    }
}
