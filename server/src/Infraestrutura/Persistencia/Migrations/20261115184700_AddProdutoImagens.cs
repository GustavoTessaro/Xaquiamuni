using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddProdutoImagens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProdutoImagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChaveArmazenamento = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NomeOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TipoConteudo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    LarguraPixels = table.Column<int>(type: "int", nullable: false),
                    AlturaPixels = table.Column<int>(type: "int", nullable: false),
                    OrdemExibicao = table.Column<int>(type: "int", nullable: false),
                    SlotGaleria = table.Column<int>(type: "int", nullable: false),
                    Principal = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdutoImagens", x => x.Id);
                    table.UniqueConstraint("AK_ProdutoImagens_ProdutoId_Id", x => new { x.ProdutoId, x.Id });
                    table.CheckConstraint("CK_ProdutoImagens_CriadoEmUtc", "DATEPART(TZOFFSET, [CriadoEmUtc]) = 0");
                    table.CheckConstraint("CK_ProdutoImagens_Dimensoes_Positive", "[LarguraPixels] > 0 AND [AlturaPixels] > 0");
                    table.CheckConstraint("CK_ProdutoImagens_Metadata_NotBlank", "LEN(LTRIM(RTRIM([ChaveArmazenamento]))) > 0 AND LEN(LTRIM(RTRIM([NomeOriginal]))) > 0 AND LEN(LTRIM(RTRIM([TipoConteudo]))) > 0");
                    table.CheckConstraint("CK_ProdutoImagens_OrdemExibicao_Valid", "[OrdemExibicao] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ProdutoImagens_SlotGaleria_Valid", "[SlotGaleria] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ProdutoImagens_TamanhoBytes_Positive", "[TamanhoBytes] > 0");
                    table.ForeignKey(
                        name: "FK_ProdutoImagens_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProdutoImagemVariantes",
                columns: table => new
                {
                    ProdutoImagemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdutoImagemVariantes", x => new { x.ProdutoImagemId, x.ProdutoVarianteId });
                    table.ForeignKey(
                        name: "FK_ProdutoImagemVariantes_ProdutoImagens_ProdutoId_ProdutoImagemId",
                        columns: x => new { x.ProdutoId, x.ProdutoImagemId },
                        principalTable: "ProdutoImagens",
                        principalColumns: new[] { "ProdutoId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProdutoImagemVariantes_ProdutoVariantes_ProdutoId_ProdutoVarianteId",
                        columns: x => new { x.ProdutoId, x.ProdutoVarianteId },
                        principalTable: "ProdutoVariantes",
                        principalColumns: new[] { "ProdutoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProdutoImagemVariantes_Produto_Variante",
                table: "ProdutoImagemVariantes",
                columns: new[] { "ProdutoId", "ProdutoVarianteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProdutoImagemVariantes_ProdutoId_ProdutoImagemId",
                table: "ProdutoImagemVariantes",
                columns: new[] { "ProdutoId", "ProdutoImagemId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoImagens_ChaveArmazenamento",
                table: "ProdutoImagens",
                column: "ChaveArmazenamento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoImagens_PrincipalPorProduto",
                table: "ProdutoImagens",
                column: "ProdutoId",
                unique: true,
                filter: "[Principal] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoImagens_ProdutoId_SlotGaleria",
                table: "ProdutoImagens",
                columns: new[] { "ProdutoId", "SlotGaleria" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProdutoImagemVariantes");

            migrationBuilder.DropTable(
                name: "ProdutoImagens");
        }
    }
}
