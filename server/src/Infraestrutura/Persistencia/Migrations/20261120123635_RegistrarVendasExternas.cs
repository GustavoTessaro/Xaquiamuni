using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarVendasExternas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VendasExternas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Canal = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DescricaoCanal = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DataVenda = table.Column<DateOnly>(type: "date", nullable: false),
                    Comprador = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: true),
                    TotalCentavos = table.Column<long>(type: "bigint", nullable: false),
                    Situacao = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Versao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanceladaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CanceladaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendasExternas", x => x.Id);
                    table.CheckConstraint("CK_VendasExternas_Total", "[TotalCentavos] > 0");
                });

            migrationBuilder.CreateTable(
                name: "ItensVendaExterna",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeProduto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NomeVariante = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    PrecoUnitarioCentavos = table.Column<long>(type: "bigint", nullable: false),
                    DescontoCentavos = table.Column<long>(type: "bigint", nullable: false),
                    SubtotalCentavos = table.Column<long>(type: "bigint", nullable: false),
                    VendaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensVendaExterna", x => x.Id);
                    table.CheckConstraint("CK_ItensVendaExterna_Valores", "[Quantidade] > 0 AND [PrecoUnitarioCentavos] >= 0 AND [DescontoCentavos] >= 0 AND [SubtotalCentavos] >= 0");
                    table.ForeignKey(
                        name: "FK_ItensVendaExterna_ProdutoVariantes_VarianteId",
                        column: x => x.VarianteId,
                        principalTable: "ProdutoVariantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensVendaExterna_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensVendaExterna_VendasExternas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "VendasExternas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperacoesVendaExterna",
                columns: table => new
                {
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Fingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesVendaExterna", x => new { x.ResponsavelId, x.Chave });
                    table.ForeignKey(
                        name: "FK_OperacoesVendaExterna_VendasExternas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "VendasExternas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecebimentosVendaExterna",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValorCentavos = table.Column<long>(type: "bigint", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Meio = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegistradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecebimentosVendaExterna", x => x.Id);
                    table.CheckConstraint("CK_RecebimentosVendaExterna_Valor", "[ValorCentavos] > 0");
                    table.ForeignKey(
                        name: "FK_RecebimentosVendaExterna_VendasExternas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "VendasExternas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BaixasVendaExterna",
                columns: table => new
                {
                    MovimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    MovimentoCancelamentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaixasVendaExterna", x => x.MovimentoId);
                    table.CheckConstraint("CK_BaixasVendaExterna_Quantidade", "[Quantidade] > 0");
                    table.ForeignKey(
                        name: "FK_BaixasVendaExterna_ItensVendaExterna_ItemId",
                        column: x => x.ItemId,
                        principalTable: "ItensVendaExterna",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BaixasVendaExterna_LotesProdutoAcabado_LoteId",
                        column: x => x.LoteId,
                        principalTable: "LotesProdutoAcabado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BaixasVendaExterna_MovimentosProdutoAcabado_MovimentoCancelamentoId",
                        column: x => x.MovimentoCancelamentoId,
                        principalTable: "MovimentosProdutoAcabado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BaixasVendaExterna_MovimentosProdutoAcabado_MovimentoId",
                        column: x => x.MovimentoId,
                        principalTable: "MovimentosProdutoAcabado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BaixasVendaExterna_ItemId_LoteId",
                table: "BaixasVendaExterna",
                columns: new[] { "ItemId", "LoteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaixasVendaExterna_LoteId",
                table: "BaixasVendaExterna",
                column: "LoteId");

            migrationBuilder.CreateIndex(
                name: "IX_BaixasVendaExterna_MovimentoCancelamentoId",
                table: "BaixasVendaExterna",
                column: "MovimentoCancelamentoId",
                unique: true,
                filter: "[MovimentoCancelamentoId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVendaExterna_ProdutoId",
                table: "ItensVendaExterna",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVendaExterna_VarianteId",
                table: "ItensVendaExterna",
                column: "VarianteId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVendaExterna_VendaId_VarianteId",
                table: "ItensVendaExterna",
                columns: new[] { "VendaId", "VarianteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperacoesVendaExterna_VendaId",
                table: "OperacoesVendaExterna",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_RecebimentosVendaExterna_ResponsavelId_Chave",
                table: "RecebimentosVendaExterna",
                columns: new[] { "ResponsavelId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecebimentosVendaExterna_VendaId",
                table: "RecebimentosVendaExterna",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_VendasExternas_DataVenda_Id",
                table: "VendasExternas",
                columns: new[] { "DataVenda", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BaixasVendaExterna");

            migrationBuilder.DropTable(
                name: "OperacoesVendaExterna");

            migrationBuilder.DropTable(
                name: "RecebimentosVendaExterna");

            migrationBuilder.DropTable(
                name: "ItensVendaExterna");

            migrationBuilder.DropTable(
                name: "VendasExternas");
        }
    }
}
