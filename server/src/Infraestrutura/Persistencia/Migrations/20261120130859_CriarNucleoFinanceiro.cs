using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CriarNucleoFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LancamentosFinanceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Categoria = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ValorCentavos = table.Column<long>(type: "bigint", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Meio = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CanceladoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CanceladoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosFinanceiros", x => x.Id);
                    table.CheckConstraint("CK_LancamentosFinanceiros_Valor", "[ValorCentavos] > 0");
                });

            migrationBuilder.CreateTable(
                name: "OperacoesFinanceiras",
                columns: table => new
                {
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Impressao = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesFinanceiras", x => new { x.ResponsavelId, x.Chave });
                });

            migrationBuilder.CreateTable(
                name: "PagamentosFornecedores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValorCentavos = table.Column<long>(type: "bigint", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Meio = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagamentosFornecedores", x => x.Id);
                    table.CheckConstraint("CK_PagamentosFornecedores_Valor", "[ValorCentavos] > 0");
                    table.ForeignKey(
                        name: "FK_PagamentosFornecedores_ComprasMateriasPrimas_CompraId",
                        column: x => x.CompraId,
                        principalTable: "ComprasMateriasPrimas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_Data_Id",
                table: "LancamentosFinanceiros",
                columns: new[] { "Data", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosFornecedores_CompraId_Data_Id",
                table: "PagamentosFornecedores",
                columns: new[] { "CompraId", "Data", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LancamentosFinanceiros");

            migrationBuilder.DropTable(
                name: "OperacoesFinanceiras");

            migrationBuilder.DropTable(
                name: "PagamentosFornecedores");
        }
    }
}
