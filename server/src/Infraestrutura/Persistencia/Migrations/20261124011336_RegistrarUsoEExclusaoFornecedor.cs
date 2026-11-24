using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarUsoEExclusaoFornecedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UtilizadoEmCompra",
                table: "Fornecedores",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Versao",
                table: "Fornecedores",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.Sql("""
                UPDATE f SET [UtilizadoEmCompra] = 1
                FROM [Fornecedores] AS f
                WHERE EXISTS (SELECT 1 FROM [ComprasMateriasPrimas] AS c WHERE c.[FornecedorId] = f.[Id]);
                """);

            migrationBuilder.Sql("""
                EXEC(N'
                CREATE TRIGGER [TR_ComprasMateriasPrimas_RegistrarUsoFornecedor]
                ON [ComprasMateriasPrimas] AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF NOT UPDATE([FornecedorId]) RETURN;

                    UPDATE f SET [UtilizadoEmCompra] = 1
                    FROM [Fornecedores] AS f
                    WHERE (f.[UtilizadoEmCompra] = 0 OR f.[UtilizadoEmCompra] IS NULL)
                    AND (
                        EXISTS (SELECT 1 FROM inserted AS i WHERE i.[FornecedorId] = f.[Id])
                        OR EXISTS (SELECT 1 FROM deleted AS d WHERE d.[FornecedorId] = f.[Id])
                    );
                END;');
                """);

            migrationBuilder.CreateTable(
                name: "OperacoesExclusaoFornecedor",
                columns: table => new
                {
                    Autor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FornecedorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Versao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesExclusaoFornecedor", x => new { x.Autor, x.Chave });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_ComprasMateriasPrimas_RegistrarUsoFornecedor];");
            migrationBuilder.DropTable(
                name: "OperacoesExclusaoFornecedor");

            migrationBuilder.DropColumn(
                name: "UtilizadoEmCompra",
                table: "Fornecedores");

            migrationBuilder.DropColumn(
                name: "Versao",
                table: "Fornecedores");
        }
    }
}
