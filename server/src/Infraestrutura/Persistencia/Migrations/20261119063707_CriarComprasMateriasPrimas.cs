using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CriarComprasMateriasPrimas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComprasMateriasPrimas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FornecedorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeFornecedor = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DataCompra = table.Column<DateOnly>(type: "date", nullable: false),
                    Situacao = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtualizadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CanceladoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Versao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChaveCriacao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImpressaoCriacao = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprasMateriasPrimas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComprasMateriasPrimas_Fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "Fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LinhasCompraMateriaPrima",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    Kit = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Quantidade = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ValorTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinhasCompraMateriaPrima", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinhasCompraMateriaPrima_ComprasMateriasPrimas_CompraId",
                        column: x => x.CompraId,
                        principalTable: "ComprasMateriasPrimas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComponentesCompraMateriaPrima",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeInsumo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApresentacaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NomeApresentacao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantidade = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Fator = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    QuantidadeBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnidadeBase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LinhaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentesCompraMateriaPrima", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComponentesCompraMateriaPrima_FormasCompraInsumo_ApresentacaoId",
                        column: x => x.ApresentacaoId,
                        principalTable: "FormasCompraInsumo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComponentesCompraMateriaPrima_InsumosEstoque_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "InsumosEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComponentesCompraMateriaPrima_LinhasCompraMateriaPrima_LinhaId",
                        column: x => x.LinhaId,
                        principalTable: "LinhasCompraMateriaPrima",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComponentesCompraMateriaPrima_ApresentacaoId",
                table: "ComponentesCompraMateriaPrima",
                column: "ApresentacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_ComponentesCompraMateriaPrima_InsumoId",
                table: "ComponentesCompraMateriaPrima",
                column: "InsumoId");

            migrationBuilder.CreateIndex(
                name: "IX_ComponentesCompraMateriaPrima_LinhaId",
                table: "ComponentesCompraMateriaPrima",
                column: "LinhaId");

            migrationBuilder.CreateIndex(
                name: "IX_ComprasMateriasPrimas_DataCompra_Id",
                table: "ComprasMateriasPrimas",
                columns: new[] { "DataCompra", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ComprasMateriasPrimas_FornecedorId",
                table: "ComprasMateriasPrimas",
                column: "FornecedorId");

            migrationBuilder.CreateIndex(
                name: "UX_ComprasMateriasPrimas_Criacao",
                table: "ComprasMateriasPrimas",
                columns: new[] { "CriadoPorId", "ChaveCriacao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinhasCompraMateriaPrima_CompraId",
                table: "LinhasCompraMateriaPrima",
                column: "CompraId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComponentesCompraMateriaPrima");

            migrationBuilder.DropTable(
                name: "LinhasCompraMateriaPrima");

            migrationBuilder.DropTable(
                name: "ComprasMateriasPrimas");
        }
    }
}
