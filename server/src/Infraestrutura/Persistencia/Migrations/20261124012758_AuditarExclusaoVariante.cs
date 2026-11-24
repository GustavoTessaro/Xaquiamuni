using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AuditarExclusaoVariante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacoesExclusaoVariante",
                columns: table => new
                {
                    Autor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Identificacao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Versao = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    InstanteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesExclusaoVariante", x => new { x.Autor, x.Chave });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacoesExclusaoVariante");
        }
    }
}
