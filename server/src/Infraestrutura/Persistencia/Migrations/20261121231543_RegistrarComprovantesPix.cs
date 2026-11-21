using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarComprovantesPix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnviosComprovantePix",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersaoAnterior = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EnviadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TamanhoBytes = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnviosComprovantePix", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnviosComprovantePix_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComprovantesPixAtivos",
                columns: table => new
                {
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprovantesPixAtivos", x => x.PedidoId);
                    table.ForeignKey(
                        name: "FK_ComprovantesPixAtivos_EnviosComprovantePix_EnvioId",
                        column: x => x.EnvioId,
                        principalTable: "EnviosComprovantePix",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComprovantesPixAtivos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprovantesPixAtivos_EnvioId",
                table: "ComprovantesPixAtivos",
                column: "EnvioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnviosComprovantePix_ClienteId_Chave",
                table: "EnviosComprovantePix",
                columns: new[] { "ClienteId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnviosComprovantePix_PedidoId",
                table: "EnviosComprovantePix",
                column: "PedidoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComprovantesPixAtivos");

            migrationBuilder.DropTable(
                name: "EnviosComprovantePix");
        }
    }
}
