using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PersistirPedidosCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pedidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PrazoPagamentoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Situacao = table.Column<short>(type: "smallint", nullable: false),
                    ModalidadeEntrega = table.Column<short>(type: "smallint", nullable: false),
                    TotalCentavos = table.Column<long>(type: "bigint", nullable: false),
                    PagamentoConfirmadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PagamentoConfirmadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreparacaoIniciadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreparacaoIniciadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RetiradaAutorizadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RetiradaAutorizadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EncerradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EncerradoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedidos", x => x.Id);
                    table.CheckConstraint("CK_Pedidos_Auditoria_Valid", "([PagamentoConfirmadoEmUtc] IS NULL AND [PagamentoConfirmadoPorId] IS NULL OR ([PagamentoConfirmadoEmUtc] IS NOT NULL AND [PagamentoConfirmadoPorId] IS NOT NULL AND [PagamentoConfirmadoPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [PagamentoConfirmadoEmUtc]) = 0)) AND ([PreparacaoIniciadaEmUtc] IS NULL AND [PreparacaoIniciadaPorId] IS NULL OR ([PreparacaoIniciadaEmUtc] IS NOT NULL AND [PreparacaoIniciadaPorId] IS NOT NULL AND [PreparacaoIniciadaPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [PreparacaoIniciadaEmUtc]) = 0)) AND ([RetiradaAutorizadaEmUtc] IS NULL AND [RetiradaAutorizadaPorId] IS NULL OR ([RetiradaAutorizadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaPorId] IS NOT NULL AND [RetiradaAutorizadaPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [RetiradaAutorizadaEmUtc]) = 0))");
                    table.CheckConstraint("CK_Pedidos_Cronologia_Valid", "([PagamentoConfirmadoEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] >= [CriadoEmUtc] AND [PagamentoConfirmadoEmUtc] < [PrazoPagamentoEmUtc])) AND ([PreparacaoIniciadaEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] >= [PagamentoConfirmadoEmUtc])) AND ([RetiradaAutorizadaEmUtc] IS NULL OR ([PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] >= [PreparacaoIniciadaEmUtc])) AND ([EncerradoEmUtc] IS NULL OR (DATEPART(TZOFFSET, [EncerradoEmUtc]) = 0 AND [EncerradoEmUtc] >= COALESCE([RetiradaAutorizadaEmUtc], [PreparacaoIniciadaEmUtc], [PagamentoConfirmadoEmUtc], [CriadoEmUtc])))");
                    table.CheckConstraint("CK_Pedidos_Encerramento_Valid", "(([Situacao] IN (5, 6) AND [EncerradoPorId] IS NOT NULL AND [EncerradoPorId] <> '00000000-0000-0000-0000-000000000000') OR ([Situacao] NOT IN (5, 6) AND [EncerradoPorId] IS NULL)) AND (([Situacao] = 6 AND [MotivoCancelamento] IS NOT NULL AND LEN(LTRIM(RTRIM([MotivoCancelamento]))) > 0) OR ([Situacao] <> 6 AND [MotivoCancelamento] IS NULL))");
                    table.CheckConstraint("CK_Pedidos_Identificadores_NotEmpty", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ClienteId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Pedidos_Modalidade_Valid", "[ModalidadeEntrega] = 1");
                    table.CheckConstraint("CK_Pedidos_Prazo_Utc24Horas", "DATEPART(TZOFFSET, [CriadoEmUtc]) = 0 AND DATEPART(TZOFFSET, [PrazoPagamentoEmUtc]) = 0 AND DATEDIFF_BIG(hour, [CriadoEmUtc], [PrazoPagamentoEmUtc]) = 24 AND CAST([CriadoEmUtc] AS time(7)) = CAST([PrazoPagamentoEmUtc] AS time(7))");
                    table.CheckConstraint("CK_Pedidos_Situacao_Valid", "([Situacao] = 1 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 2 AND [PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 3 AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 4 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 5 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NOT NULL) OR ([Situacao] = 6 AND [EncerradoEmUtc] IS NOT NULL AND ([PagamentoConfirmadoEmUtc] IS NOT NULL OR [EncerradoEmUtc] < [PrazoPagamentoEmUtc])) OR ([Situacao] = 7 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NOT NULL AND [EncerradoEmUtc] >= [PrazoPagamentoEmUtc])");
                    table.CheckConstraint("CK_Pedidos_Total_NonNegative", "[TotalCentavos] >= 0");
                    table.ForeignKey(
                        name: "FK_Pedidos_ClientePerfis_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "ClientePerfis",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItensPedido",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProdutoVarianteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeProduto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NomeVariante = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    PrecoUnitarioCentavos = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensPedido", x => x.Id);
                    table.CheckConstraint("CK_ItensPedido_Identificadores_NotEmpty", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [PedidoId] <> '00000000-0000-0000-0000-000000000000' AND [ProdutoId] <> '00000000-0000-0000-0000-000000000000' AND [ProdutoVarianteId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ItensPedido_Nomes_NotBlank", "LEN(LTRIM(RTRIM([NomeProduto]))) > 0 AND LEN(LTRIM(RTRIM([NomeVariante]))) > 0");
                    table.CheckConstraint("CK_ItensPedido_PrecoCentavos_Valid", "[PrecoUnitarioCentavos] >= 0 AND [PrecoUnitarioCentavos] <= CONVERT(bigint, 9223372036854775807) / NULLIF(CONVERT(bigint, [Quantidade]), 0)");
                    table.CheckConstraint("CK_ItensPedido_Quantidade_Positive", "[Quantidade] > 0");
                    table.ForeignKey(
                        name: "FK_ItensPedido_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensPedido_ProdutoVariantes_ProdutoId_ProdutoVarianteId",
                        columns: x => new { x.ProdutoId, x.ProdutoVarianteId },
                        principalTable: "ProdutoVariantes",
                        principalColumns: new[] { "ProdutoId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItensPedido_PedidoId_ProdutoVarianteId",
                table: "ItensPedido",
                columns: new[] { "PedidoId", "ProdutoVarianteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItensPedido_ProdutoId_ProdutoVarianteId",
                table: "ItensPedido",
                columns: new[] { "ProdutoId", "ProdutoVarianteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_ClienteId_CriadoEmUtc",
                table: "Pedidos",
                columns: new[] { "ClienteId", "CriadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Situacao_PrazoPagamentoEmUtc",
                table: "Pedidos",
                columns: new[] { "Situacao", "PrazoPagamentoEmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensPedido");

            migrationBuilder.DropTable(
                name: "Pedidos");
        }
    }
}
