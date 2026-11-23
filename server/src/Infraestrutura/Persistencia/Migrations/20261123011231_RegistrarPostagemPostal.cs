using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarPostagemPostal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_SaidaEntrega_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos");

            migrationBuilder.AddColumn<string>(
                name: "CodigoRastreamento",
                table: "Pedidos",
                type: "varchar(13)",
                unicode: false,
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServicoPostalUtilizado",
                table: "Pedidos",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperacoesEntregaPostal",
                columns: table => new
                {
                    AdministradorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Impressao = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Etapa = table.Column<short>(type: "smallint", nullable: false),
                    RealizadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resultado = table.Column<short>(type: "smallint", nullable: false),
                    CodigoAnterior = table.Column<string>(type: "varchar(13)", unicode: false, maxLength: 13, nullable: true),
                    CodigoNovo = table.Column<string>(type: "varchar(13)", unicode: false, maxLength: 13, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesEntregaPostal", x => new { x.AdministradorId, x.Chave });
                    table.ForeignKey(
                        name: "FK_OperacoesEntregaPostal_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos",
                sql: "((([ModalidadeEntrega] = 1 AND [FreteCentavos] = 0) OR ([ModalidadeEntrega] = 2 AND [Entrega] IS NOT NULL)) AND [CotacaoCorreiosId] IS NULL AND [RevisaoCotacaoCorreios] IS NULL) OR ([ModalidadeEntrega] = 3 AND [Entrega] IS NOT NULL AND [CotacaoCorreiosId] IS NOT NULL AND [RevisaoCotacaoCorreios] IS NOT NULL AND [RevisaoCotacaoCorreios] > 0 AND [RetiradaAutorizadaEmUtc] IS NULL AND [Situacao] NOT IN (4, 8))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Postagem_Valid",
                table: "Pedidos",
                sql: "(([ModalidadeEntrega] <> 3 OR [SaidaEntregaEmUtc] IS NULL) AND [ServicoPostalUtilizado] IS NULL AND [CodigoRastreamento] IS NULL) OR ([ModalidadeEntrega] = 3 AND [SaidaEntregaEmUtc] IS NOT NULL AND [ServicoPostalUtilizado] IS NOT NULL AND LEN(LTRIM(RTRIM([ServicoPostalUtilizado]))) > 0 AND ([CodigoRastreamento] IS NULL OR LEN([CodigoRastreamento]) = 13))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_SaidaEntrega_Valid",
                table: "Pedidos",
                sql: "([SaidaEntregaEmUtc] IS NULL AND [Situacao] NOT IN (8, 9) AND ([Situacao] <> 5 OR [ModalidadeEntrega] = 1)) OR ([SaidaEntregaEmUtc] IS NOT NULL AND (([ModalidadeEntrega] = 2 AND [Situacao] IN (5, 8)) OR ([ModalidadeEntrega] = 3 AND [Situacao] IN (5, 9))) AND [RetiradaAutorizadaEmUtc] IS NULL AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [SaidaEntregaEmUtc] >= [PreparacaoIniciadaEmUtc])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos",
                sql: "([Situacao] = 1 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 2 AND [PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 3 AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 4 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 5 AND COALESCE([RetiradaAutorizadaEmUtc], [SaidaEntregaEmUtc]) IS NOT NULL AND [EncerradoEmUtc] IS NOT NULL) OR ([Situacao] IN (8, 9) AND [SaidaEntregaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 6 AND [EncerradoEmUtc] IS NOT NULL AND ([PagamentoConfirmadoEmUtc] IS NOT NULL OR [EncerradoEmUtc] < [PrazoPagamentoEmUtc])) OR ([Situacao] = 7 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NOT NULL AND [EncerradoEmUtc] >= [PrazoPagamentoEmUtc])");

            migrationBuilder.CreateIndex(
                name: "IX_OperacoesEntregaPostal_PedidoId",
                table: "OperacoesEntregaPostal",
                column: "PedidoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacoesEntregaPostal");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Postagem_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_SaidaEntrega_Valid",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "CodigoRastreamento",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "ServicoPostalUtilizado",
                table: "Pedidos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Modalidade_Valid",
                table: "Pedidos",
                sql: "((([ModalidadeEntrega] = 1 AND [FreteCentavos] = 0) OR ([ModalidadeEntrega] = 2 AND [Entrega] IS NOT NULL)) AND [CotacaoCorreiosId] IS NULL AND [RevisaoCotacaoCorreios] IS NULL) OR ([ModalidadeEntrega] = 3 AND [Entrega] IS NOT NULL AND [CotacaoCorreiosId] IS NOT NULL AND [RevisaoCotacaoCorreios] IS NOT NULL AND [RevisaoCotacaoCorreios] > 0 AND [RetiradaAutorizadaEmUtc] IS NULL AND [Situacao] NOT IN (4, 5, 8))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_SaidaEntrega_Valid",
                table: "Pedidos",
                sql: "([SaidaEntregaEmUtc] IS NULL AND [Situacao] <> 8 AND ([Situacao] <> 5 OR [ModalidadeEntrega] = 1)) OR ([SaidaEntregaEmUtc] IS NOT NULL AND [ModalidadeEntrega] = 2 AND [Situacao] IN (5, 8) AND [RetiradaAutorizadaEmUtc] IS NULL AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [SaidaEntregaEmUtc] >= [PreparacaoIniciadaEmUtc])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_Situacao_Valid",
                table: "Pedidos",
                sql: "([Situacao] = 1 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 2 AND [PagamentoConfirmadoEmUtc] IS NOT NULL AND [PreparacaoIniciadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 3 AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [RetiradaAutorizadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 4 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 5 AND COALESCE([RetiradaAutorizadaEmUtc], [SaidaEntregaEmUtc]) IS NOT NULL AND [EncerradoEmUtc] IS NOT NULL) OR ([Situacao] = 8 AND [SaidaEntregaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) OR ([Situacao] = 6 AND [EncerradoEmUtc] IS NOT NULL AND ([PagamentoConfirmadoEmUtc] IS NOT NULL OR [EncerradoEmUtc] < [PrazoPagamentoEmUtc])) OR ([Situacao] = 7 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NOT NULL AND [EncerradoEmUtc] >= [PrazoPagamentoEmUtc])");
        }
    }
}
