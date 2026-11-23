using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CriarPromocoesFrete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromocoesFrete",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<long>(type: "bigint", nullable: false),
                    Modalidades = table.Column<int>(type: "int", nullable: false),
                    SubtotalMinimoCentavos = table.Column<long>(type: "bigint", nullable: false),
                    InicioUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FimUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    CriadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AlteradaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlteradaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromocoesFrete", x => x.Id);
                    table.CheckConstraint("CK_PromocoesFrete_Modalidades", "[Modalidades] IN (1, 2, 3)");
                    table.CheckConstraint("CK_PromocoesFrete_NomeVersao", "LEN(LTRIM(RTRIM([Nome]))) > 0 AND [Versao] > 0");
                    table.CheckConstraint("CK_PromocoesFrete_Valores", "[SubtotalMinimoCentavos] >= 0 AND (([Tipo] = 1 AND [Valor] BETWEEN 1 AND 10000) OR ([Tipo] = 2 AND [Valor] > 0) OR ([Tipo] = 3 AND [Valor] = 0))");
                    table.CheckConstraint("CK_PromocoesFrete_Vigencia", "([InicioUtc] IS NULL OR DATEPART(TZOFFSET, [InicioUtc]) = 0) AND ([FimUtc] IS NULL OR DATEPART(TZOFFSET, [FimUtc]) = 0) AND ([InicioUtc] IS NULL OR [FimUtc] IS NULL OR [InicioUtc] < [FimUtc])");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromocoesFrete_Ativa_FimUtc",
                table: "PromocoesFrete",
                columns: new[] { "Ativa", "FimUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromocoesFrete_CriadaEmUtc_Id",
                table: "PromocoesFrete",
                columns: new[] { "CriadaEmUtc", "Id" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromocoesFrete");
        }
    }
}
