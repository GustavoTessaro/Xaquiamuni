using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PermitirEstoqueSemValidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LotesProdutoAcabado_ValidadeOrigem_Valid",
                table: "LotesProdutoAcabado");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DataValidade", table: "LotesProdutoAcabado", type: "date", nullable: true,
                oldClrType: typeof(DateOnly), oldType: "date");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LotesProdutoAcabado_ValidadeOrigem_Valid",
                table: "LotesProdutoAcabado",
                sql: "([OrigemValidade] = 1 AND [DataValidade] IS NOT NULL AND [PrazoValidadeAplicadoDias] IS NOT NULL AND [PrazoValidadeAplicadoDias] >= 0) OR ([OrigemValidade] = 2 AND [PrazoValidadeAplicadoDias] IS NULL AND [DataValidade] IS NOT NULL) OR ([OrigemValidade] = 3 AND [PrazoValidadeAplicadoDias] IS NULL AND [DataValidade] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LotesProdutoAcabado_ValidadeOrigem_Valid",
                table: "LotesProdutoAcabado");

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [LotesProdutoAcabado] WHERE [DataValidade] IS NULL)
                    THROW 51000, 'Downgrade indisponivel enquanto houver lotes sem validade.', 1;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DataValidade", table: "LotesProdutoAcabado", type: "date", nullable: false,
                oldClrType: typeof(DateOnly), oldType: "date", oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LotesProdutoAcabado_ValidadeOrigem_Valid",
                table: "LotesProdutoAcabado",
                sql: "([OrigemValidade] = 1 AND [PrazoValidadeAplicadoDias] IS NOT NULL AND [PrazoValidadeAplicadoDias] >= 0) OR ([OrigemValidade] = 2 AND [PrazoValidadeAplicadoDias] IS NULL)");
        }
    }
}
