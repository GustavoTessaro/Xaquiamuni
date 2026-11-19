using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CadastrarReceitasProducao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceitasProducao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtualizadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtualizadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceitasProducao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceitasProducao_ProdutoVariantes_ProdutoVarianteId",
                        column: x => x.ProdutoVarianteId,
                        principalTable: "ProdutoVariantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceitasProducaoVersoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Rendimento = table.Column<int>(type: "int", nullable: false),
                    CriadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceitaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceitasProducaoVersoes", x => x.Id);
                    table.CheckConstraint("CK_ReceitasProducaoVersoes_Positiva", "[Numero] > 0 AND [Rendimento] > 0");
                    table.ForeignKey(
                        name: "FK_ReceitasProducaoVersoes_ReceitasProducao_ReceitaId",
                        column: x => x.ReceitaId,
                        principalTable: "ReceitasProducao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItensReceitaProducao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeInsumo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnidadeBase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    VersaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensReceitaProducao", x => x.Id);
                    table.CheckConstraint("CK_ItensReceitaProducao_Quantidade", "[Quantidade] > 0 AND ([UnidadeBase] IN ('Grama','Mililitro') OR ([UnidadeBase] = 'Unidade' AND [Quantidade] = FLOOR([Quantidade])))");
                    table.ForeignKey(
                        name: "FK_ItensReceitaProducao_InsumosEstoque_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "InsumosEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensReceitaProducao_ReceitasProducaoVersoes_VersaoId",
                        column: x => x.VersaoId,
                        principalTable: "ReceitasProducaoVersoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItensReceitaProducao_InsumoId",
                table: "ItensReceitaProducao",
                column: "InsumoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensReceitaProducao_VersaoId_InsumoId",
                table: "ItensReceitaProducao",
                columns: new[] { "VersaoId", "InsumoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReceitasProducao_VarianteAtiva",
                table: "ReceitasProducao",
                column: "ProdutoVarianteId",
                unique: true,
                filter: "[Ativa] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ReceitasProducaoVersoes_ReceitaId_Numero",
                table: "ReceitasProducaoVersoes",
                columns: new[] { "ReceitaId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensReceitaProducao");

            migrationBuilder.DropTable(
                name: "ReceitasProducaoVersoes");

            migrationBuilder.DropTable(
                name: "ReceitasProducao");
        }
    }
}
