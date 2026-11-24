using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AuditarExclusaoReceitas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacoesExclusaoReceita",
                columns: table => new
                {
                    Autor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceitaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    RevisoesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesExclusaoReceita", x => new { x.Autor, x.Chave });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacoesExclusaoReceita");
        }
    }
}
