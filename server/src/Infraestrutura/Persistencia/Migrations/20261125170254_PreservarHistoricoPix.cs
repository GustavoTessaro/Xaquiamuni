using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PreservarHistoricoPix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Revisao",
                table: "ConfiguracoesPixManual",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "AlteracoesConfiguracaoPix",
                columns: table => new
                {
                    AutorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResultadoJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegistradaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlteracoesConfiguracaoPix", x => new { x.AutorId, x.Chave });
                });

            migrationBuilder.CreateTable(
                name: "SnapshotsPixPedidos",
                columns: table => new
                {
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revisao = table.Column<long>(type: "bigint", nullable: true),
                    DadosJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotsPixPedidos", x => x.PedidoId);
                    table.ForeignKey(
                        name: "FK_SnapshotsPixPedidos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlteracoesConfiguracaoPix");

            migrationBuilder.DropTable(
                name: "SnapshotsPixPedidos");

            migrationBuilder.DropColumn(
                name: "Revisao",
                table: "ConfiguracoesPixManual");
        }
    }
}
