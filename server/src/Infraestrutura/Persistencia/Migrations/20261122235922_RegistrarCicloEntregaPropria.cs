using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarCicloEntregaPropria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Auditoria_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Cronologia_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SaidaEntregaEmUtc",
                table: "Pedidos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaidaEntregaPorId",
                table: "Pedidos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperacoesEntregaPropria",
                columns: table => new
                {
                    AdministradorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersaoEsperada = table.Column<byte[]>(type: "binary(8)", fixedLength: true, maxLength: 8, nullable: false),
                    Etapa = table.Column<short>(type: "smallint", nullable: false),
                    RealizadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesEntregaPropria", x => new { x.AdministradorId, x.Chave });
                    table.ForeignKey(
                        name: "FK_OperacoesEntregaPropria_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Auditoria_Valid",
                table: "Pedidos",
                sql: "([PagamentoConfirmadoEmUtc] IS NULL AND [PagamentoConfirmadoPorId] IS NULL OR ([PagamentoConfirmadoEmUtc] IS NOT NULL AND [PagamentoConfirmadoPorId] IS NOT NULL AND [PagamentoConfirmadoPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [PagamentoConfirmadoEmUtc]) = 0)) AND ([PreparacaoIniciadaEmUtc] IS NULL AND [PreparacaoIniciadaPorId] IS NULL OR ([PreparacaoIniciadaEmUtc] IS NOT NULL AND [PreparacaoIniciadaPorId] IS NOT NULL AND [PreparacaoIniciadaPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [PreparacaoIniciadaEmUtc]) = 0)) AND ([RetiradaAutorizadaEmUtc] IS NULL AND [RetiradaAutorizadaPorId] IS NULL OR ([RetiradaAutorizadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaPorId] IS NOT NULL AND [RetiradaAutorizadaPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [RetiradaAutorizadaEmUtc]) = 0)) AND ([SaidaEntregaEmUtc] IS NULL AND [SaidaEntregaPorId] IS NULL OR ([SaidaEntregaEmUtc] IS NOT NULL AND [SaidaEntregaPorId] IS NOT NULL AND [SaidaEntregaPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [SaidaEntregaEmUtc]) = 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Cronologia_Valid",
                table: "Pedidos",
                sql: "([PagamentoConfirmadoEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] >= [CriadoEmUtc] AND [PagamentoConfirmadoEmUtc] < [PrazoPagamentoEmUtc])) AND ([PreparacaoIniciadaEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] >= [PagamentoConfirmadoEmUtc])) AND ([RetiradaAutorizadaEmUtc] IS NULL OR ([PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] >= [PreparacaoIniciadaEmUtc])) AND ([EncerradoEmUtc] IS NULL OR (DATEPART(TZOFFSET, [EncerradoEmUtc]) = 0 AND [EncerradoEmUtc] >= COALESCE([SaidaEntregaEmUtc], [RetiradaAutorizadaEmUtc], [PreparacaoIniciadaEmUtc], [PagamentoConfirmadoEmUtc], [CriadoEmUtc])))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_SaidaEntrega_Valid",
                table: "Pedidos",
                sql: "([SaidaEntregaEmUtc] IS NULL AND [Situacao] <> 8 AND ([Situacao] <> 5 OR [ModalidadeEntrega] = 1)) OR ([SaidaEntregaEmUtc] IS NOT NULL AND [ModalidadeEntrega] = 2 AND [Situacao] IN (5, 8) AND [RetiradaAutorizadaEmUtc] IS NULL AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [SaidaEntregaEmUtc] >= [PreparacaoIniciadaEmUtc])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos",
                sql: "([Situacao] = 1 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 2 AND [PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 3 AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 4 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 5 AND COALESCE([RetiradaAutorizadaEmUtc], [SaidaEntregaEmUtc]) IS NOT NULL AND [EncerradoEmUtc] IS NOT NULL) OR ([Situacao] = 8 AND [SaidaEntregaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 6 AND [EncerradoEmUtc] IS NOT NULL AND ([PagamentoConfirmadoEmUtc] IS NOT NULL OR [EncerradoEmUtc] < [PrazoPagamentoEmUtc])) OR ([Situacao] = 7 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NOT NULL AND [EncerradoEmUtc] >= [PrazoPagamentoEmUtc])");

            migrationBuilder.CreateIndex(
                name: "IX_OperacoesEntregaPropria_PedidoId",
                table: "OperacoesEntregaPropria",
                column: "PedidoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacoesEntregaPropria");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Auditoria_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Cronologia_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_SaidaEntrega_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "SaidaEntregaEmUtc",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "SaidaEntregaPorId",
                table: "Pedidos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Auditoria_Valid",
                table: "Pedidos",
                sql: "([PagamentoConfirmadoEmUtc] IS NULL AND [PagamentoConfirmadoPorId] IS NULL OR ([PagamentoConfirmadoEmUtc] IS NOT NULL AND [PagamentoConfirmadoPorId] IS NOT NULL AND [PagamentoConfirmadoPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [PagamentoConfirmadoEmUtc]) = 0)) AND ([PreparacaoIniciadaEmUtc] IS NULL AND [PreparacaoIniciadaPorId] IS NULL OR ([PreparacaoIniciadaEmUtc] IS NOT NULL AND [PreparacaoIniciadaPorId] IS NOT NULL AND [PreparacaoIniciadaPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [PreparacaoIniciadaEmUtc]) = 0)) AND ([RetiradaAutorizadaEmUtc] IS NULL AND [RetiradaAutorizadaPorId] IS NULL OR ([RetiradaAutorizadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaPorId] IS NOT NULL AND [RetiradaAutorizadaPorId] <> '00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET, [RetiradaAutorizadaEmUtc]) = 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Cronologia_Valid",
                table: "Pedidos",
                sql: "([PagamentoConfirmadoEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] >= [CriadoEmUtc] AND [PagamentoConfirmadoEmUtc] < [PrazoPagamentoEmUtc])) AND ([PreparacaoIniciadaEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] >= [PagamentoConfirmadoEmUtc])) AND ([RetiradaAutorizadaEmUtc] IS NULL OR ([PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] >= [PreparacaoIniciadaEmUtc])) AND ([EncerradoEmUtc] IS NULL OR (DATEPART(TZOFFSET, [EncerradoEmUtc]) = 0 AND [EncerradoEmUtc] >= COALESCE([RetiradaAutorizadaEmUtc], [PreparacaoIniciadaEmUtc], [PagamentoConfirmadoEmUtc], [CriadoEmUtc])))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos",
                sql: "([Situacao] = 1 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 2 AND [PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 3 AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 4 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 5 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NOT NULL) OR ([Situacao] = 6 AND [EncerradoEmUtc] IS NOT NULL AND ([PagamentoConfirmadoEmUtc] IS NOT NULL OR [EncerradoEmUtc] < [PrazoPagamentoEmUtc])) OR ([Situacao] = 7 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NOT NULL AND [EncerradoEmUtc] >= [PrazoPagamentoEmUtc])");
        }
    }
}
