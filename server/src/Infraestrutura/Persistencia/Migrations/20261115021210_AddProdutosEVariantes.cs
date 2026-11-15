using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddProdutosEVariantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Produtos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CategoriaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Publicado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produtos", x => x.Id);
                    table.CheckConstraint("CK_Produtos_Nome_NotBlank", "LEN(LTRIM(RTRIM([Nome]))) > 0");
                    table.ForeignKey(
                        name: "FK_Produtos_Categorias_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "Categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProdutoVariantes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Identificacao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdentificacaoNormalizada = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UnidadeVenda = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PrecoVenda = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    Padrao = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdutoVariantes", x => x.Id);
                    table.CheckConstraint("CK_ProdutoVariantes_Default_Active", "[Padrao] = 0 OR [Ativa] = 1");
                    table.CheckConstraint("CK_ProdutoVariantes_Identificacao_NotBlank", "LEN(LTRIM(RTRIM([Identificacao]))) > 0");
                    table.CheckConstraint("CK_ProdutoVariantes_PrecoVenda_Positive", "[PrecoVenda] > 0");
                    table.CheckConstraint("CK_ProdutoVariantes_UnidadeVenda_NotBlank", "LEN(LTRIM(RTRIM([UnidadeVenda]))) > 0");
                    table.ForeignKey(
                        name: "FK_ProdutoVariantes_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CategoriaId",
                table: "Produtos",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoVariantes_DefaultPerProduto",
                table: "ProdutoVariantes",
                column: "ProdutoId",
                unique: true,
                filter: "[Padrao] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ProdutoVariantes_ProdutoId_IdentificacaoNormalizada",
                table: "ProdutoVariantes",
                columns: new[] { "ProdutoId", "IdentificacaoNormalizada" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProdutoVariantes");

            migrationBuilder.DropTable(
                name: "Produtos");
        }
    }
}
