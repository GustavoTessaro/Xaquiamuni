using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurarAtendimentoLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CidadesEntregaPropria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodigoIbge = table.Column<string>(type: "nchar(7)", fixedLength: true, maxLength: 7, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Uf = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    TarifaCentavos = table.Column<long>(type: "bigint", nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    Revisao = table.Column<int>(type: "int", nullable: false),
                    CriadoPor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AlteradoPor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlteradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CidadesEntregaPropria", x => x.Id);
                    table.CheckConstraint("CK_CidadesEntregaPropria_Tarifa", "[TarifaCentavos] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracoesAtendimento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RetiradaHabilitada = table.Column<bool>(type: "bit", nullable: false),
                    EntregaPropriaHabilitada = table.Column<bool>(type: "bit", nullable: false),
                    EnderecoRetirada = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    InstrucoesRetirada = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    WhatsApp = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    WhatsAppConfirmado = table.Column<bool>(type: "bit", nullable: false),
                    WhatsAppHabilitado = table.Column<bool>(type: "bit", nullable: false),
                    Revisao = table.Column<int>(type: "int", nullable: false),
                    AdministradorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlteradoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesAtendimento", x => x.Id);
                    table.CheckConstraint("CK_ConfiguracoesAtendimento_Modalidade", "[RetiradaHabilitada] = 1 OR [EntregaPropriaHabilitada] = 1");
                    table.CheckConstraint("CK_ConfiguracoesAtendimento_Unica", "[Id] = 1");
                    table.CheckConstraint("CK_ConfiguracoesAtendimento_WhatsApp", "([WhatsAppConfirmado] = 0 OR [WhatsApp] IS NOT NULL) AND ([WhatsAppHabilitado] = 0 OR [WhatsAppConfirmado] = 1)");
                });

            migrationBuilder.CreateTable(
                name: "OperacoesAtendimento",
                columns: table => new
                {
                    Autor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Impressao = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Operacao = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CidadeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RegistradaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResultadoJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesAtendimento", x => new { x.Autor, x.Chave });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CidadesEntregaPropria_CodigoIbge",
                table: "CidadesEntregaPropria",
                column: "CodigoIbge",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CidadesEntregaPropria");

            migrationBuilder.DropTable(
                name: "ConfiguracoesAtendimento");

            migrationBuilder.DropTable(
                name: "OperacoesAtendimento");
        }
    }
}
