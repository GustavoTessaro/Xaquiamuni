using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AuditarExclusaoProdutoELimpezaImagens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LimpezasImagensProdutos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ProximaTentativaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LimpezasImagensProdutos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperacoesExclusaoProduto",
                columns: table => new
                {
                    Autor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VariantesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Versao = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    InstanteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesExclusaoProduto", x => new { x.Autor, x.Chave });
                });

            migrationBuilder.CreateIndex(
                name: "IX_LimpezasImagensProdutos_ProximaTentativaUtc_Id",
                table: "LimpezasImagensProdutos",
                columns: new[] { "ProximaTentativaUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LimpezasImagensProdutos");

            migrationBuilder.DropTable(
                name: "OperacoesExclusaoProduto");
        }
    }
}
