using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ReservarMateriasPrimasProducao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrdensProducao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceitaVersaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantidadePlanejada = table.Column<int>(type: "int", nullable: false),
                    Situacao = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IniciadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IniciadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EncerradaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EncerradaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdensProducao", x => x.Id);
                    table.CheckConstraint("CK_OrdensProducao_Valida", "[QuantidadePlanejada] > 0 AND (([Situacao] = 'EmAndamento' AND [EncerradaEmUtc] IS NULL AND [EncerradaPorId] IS NULL) OR ([Situacao] IN ('Concluida', 'Cancelada') AND [EncerradaEmUtc] IS NOT NULL AND [EncerradaPorId] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_OrdensProducao_ProdutoVariantes_ProdutoVarianteId",
                        column: x => x.ProdutoVarianteId,
                        principalTable: "ProdutoVariantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrdensProducao_ReceitasProducaoVersoes_ReceitaVersaoId",
                        column: x => x.ReceitaVersaoId,
                        principalTable: "ReceitasProducaoVersoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NecessidadesOrdemProducao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrdemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeInsumo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnidadeBase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NecessidadesOrdemProducao", x => x.Id);
                    table.CheckConstraint("CK_NecessidadesOrdemProducao_Quantidade", "[Quantidade] > 0 AND ([UnidadeBase] IN ('Grama','Mililitro') OR ([UnidadeBase] = 'Unidade' AND [Quantidade] = FLOOR([Quantidade])))");
                    table.ForeignKey(
                        name: "FK_NecessidadesOrdemProducao_InsumosEstoque_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "InsumosEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NecessidadesOrdemProducao_OrdensProducao_OrdemId",
                        column: x => x.OrdemId,
                        principalTable: "OrdensProducao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlocacoesMateriasPrimas",
                columns: table => new
                {
                    NecessidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaldoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reservada = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Consumida = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlocacoesMateriasPrimas", x => new { x.NecessidadeId, x.SaldoId });
                    table.CheckConstraint("CK_AlocacoesMateriasPrimas_Quantidade", "[Reservada] > 0 AND [Consumida] >= 0 AND [Consumida] <= [Reservada]");
                    table.ForeignKey(
                        name: "FK_AlocacoesMateriasPrimas_NecessidadesOrdemProducao_NecessidadeId",
                        column: x => x.NecessidadeId,
                        principalTable: "NecessidadesOrdemProducao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlocacoesMateriasPrimas_SaldosInsumo_SaldoId",
                        column: x => x.SaldoId,
                        principalTable: "SaldosInsumo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlocacoesMateriasPrimas_SaldoId",
                table: "AlocacoesMateriasPrimas",
                column: "SaldoId");

            migrationBuilder.CreateIndex(
                name: "IX_NecessidadesOrdemProducao_InsumoId",
                table: "NecessidadesOrdemProducao",
                column: "InsumoId");

            migrationBuilder.CreateIndex(
                name: "IX_NecessidadesOrdemProducao_OrdemId_InsumoId",
                table: "NecessidadesOrdemProducao",
                columns: new[] { "OrdemId", "InsumoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_ProdutoVarianteId",
                table: "OrdensProducao",
                column: "ProdutoVarianteId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_ReceitaVersaoId",
                table: "OrdensProducao",
                column: "ReceitaVersaoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlocacoesMateriasPrimas");

            migrationBuilder.DropTable(
                name: "NecessidadesOrdemProducao");

            migrationBuilder.DropTable(
                name: "OrdensProducao");
        }
    }
}
