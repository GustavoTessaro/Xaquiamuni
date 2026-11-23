using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class VersionarEExcluirCategorias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Versao",
                table: "Categorias",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.CreateTable(
                name: "OperacoesExclusaoCategoria",
                columns: table => new
                {
                    AutorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeCategoria = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VersaoEsperada = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExcluidaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesExclusaoCategoria", x => new { x.AutorId, x.Chave });
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacoesExclusaoCategoria_CategoriaId",
                table: "OperacoesExclusaoCategoria",
                column: "CategoriaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacoesExclusaoCategoria");

            migrationBuilder.DropColumn(
                name: "Versao",
                table: "Categorias");
        }
    }
}
