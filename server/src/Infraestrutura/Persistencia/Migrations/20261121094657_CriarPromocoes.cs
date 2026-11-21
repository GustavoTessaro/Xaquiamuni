using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CriarPromocoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacoesPromocao",
                columns: table => new
                {
                    AdministradorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Impressao = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesPromocao", x => new { x.AdministradorId, x.Chave });
                });

            migrationBuilder.CreateTable(
                name: "Promocoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Escopo = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Valor = table.Column<long>(type: "bigint", nullable: false),
                    InicioUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FimUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    CriadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AlteradaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlteradaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promocoes", x => x.Id);
                    table.UniqueConstraint("AK_Promocoes_Id_ProdutoId", x => new { x.Id, x.ProdutoId });
                    table.CheckConstraint("CK_Promocoes_Escopo", "[Escopo] IN ('TodasVariantes', 'VariantesSelecionadas')");
                    table.CheckConstraint("CK_Promocoes_Valor", "[Valor] > 0 AND ([Tipo] = 'PrecoFixo' OR ([Tipo] = 'Percentual' AND [Valor] < 10000))");
                    table.CheckConstraint("CK_Promocoes_Vigencia", "[InicioUtc] IS NULL OR [FimUtc] IS NULL OR [InicioUtc] < [FimUtc]");
                    table.ForeignKey(
                        name: "FK_Promocoes_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromocoesVariantes",
                columns: table => new
                {
                    PromocaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromocoesVariantes", x => new { x.PromocaoId, x.VarianteId });
                    table.ForeignKey(
                        name: "FK_PromocoesVariantes_ProdutoVariantes_ProdutoId_VarianteId",
                        columns: x => new { x.ProdutoId, x.VarianteId },
                        principalTable: "ProdutoVariantes",
                        principalColumns: new[] { "ProdutoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromocoesVariantes_Promocoes_PromocaoId_ProdutoId",
                        columns: x => new { x.PromocaoId, x.ProdutoId },
                        principalTable: "Promocoes",
                        principalColumns: new[] { "Id", "ProdutoId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Promocoes_ProdutoId_Ativa",
                table: "Promocoes",
                columns: new[] { "ProdutoId", "Ativa" });

            migrationBuilder.CreateIndex(
                name: "IX_PromocoesVariantes_ProdutoId_VarianteId",
                table: "PromocoesVariantes",
                columns: new[] { "ProdutoId", "VarianteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromocoesVariantes_PromocaoId_ProdutoId",
                table: "PromocoesVariantes",
                columns: new[] { "PromocaoId", "ProdutoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacoesPromocao");

            migrationBuilder.DropTable(
                name: "PromocoesVariantes");

            migrationBuilder.DropTable(
                name: "Promocoes");
        }
    }
}
