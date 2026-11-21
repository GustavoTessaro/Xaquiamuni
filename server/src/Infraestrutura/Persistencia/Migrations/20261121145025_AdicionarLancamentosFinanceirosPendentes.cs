using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarLancamentosFinanceirosPendentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Operacao",
                table: "OperacoesFinanceiras",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Meio",
                table: "LancamentosFinanceiros",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Data",
                table: "LancamentosFinanceiros",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<string>(
                name: "Contraparte",
                table: "LancamentosFinanceiros",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacoes",
                table: "LancamentosFinanceiros",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QuitadoEmUtc",
                table: "LancamentosFinanceiros",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuitadoPorId",
                table: "LancamentosFinanceiros",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Vencimento",
                table: "LancamentosFinanceiros",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_Tipo_Vencimento_Id",
                table: "LancamentosFinanceiros",
                columns: new[] { "Tipo", "Vencimento", "Id" },
                filter: "[Data] IS NULL AND [CanceladoEmUtc] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LancamentosFinanceiros_Realizacao",
                table: "LancamentosFinanceiros",
                sql: "([Data] IS NULL AND [Meio] IS NULL AND [Vencimento] IS NOT NULL\n    AND [QuitadoPorId] IS NULL AND [QuitadoEmUtc] IS NULL)\nOR ([Data] IS NOT NULL AND [Meio] IS NOT NULL\n    AND (([Vencimento] IS NULL AND [QuitadoPorId] IS NULL AND [QuitadoEmUtc] IS NULL)\n        OR ([Vencimento] IS NOT NULL AND [QuitadoPorId] IS NOT NULL AND [QuitadoEmUtc] IS NOT NULL\n            AND [CanceladoEmUtc] IS NULL)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [LancamentosFinanceiros]
                    WHERE [Vencimento] IS NOT NULL OR [Contraparte] IS NOT NULL OR [Observacoes] IS NOT NULL)
                    THROW 51001, 'Rollback would discard financial data.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_LancamentosFinanceiros_Tipo_Vencimento_Id",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LancamentosFinanceiros_Realizacao",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "Operacao",
                table: "OperacoesFinanceiras");

            migrationBuilder.DropColumn(
                name: "Contraparte",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "Observacoes",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "QuitadoEmUtc",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "QuitadoPorId",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "Vencimento",
                table: "LancamentosFinanceiros");

            migrationBuilder.AlterColumn<string>(
                name: "Meio",
                table: "LancamentosFinanceiros",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Data",
                table: "LancamentosFinanceiros",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
