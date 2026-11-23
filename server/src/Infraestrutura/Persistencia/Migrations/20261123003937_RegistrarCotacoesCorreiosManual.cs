using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarCotacoesCorreiosManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CotacaoManualCorreiosHabilitada",
                table: "ConfiguracoesAtendimento",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ValidadePropostaCorreiosHoras",
                table: "ConfiguracoesAtendimento",
                type: "int",
                nullable: false,
                defaultValue: 48);

            migrationBuilder.CreateTable(
                name: "SolicitacoesCorreios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ItensJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EnderecoJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Impressao = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    Situacao = table.Column<short>(type: "smallint", nullable: false),
                    Revisao = table.Column<int>(type: "int", nullable: true),
                    RevisaoAceita = table.Column<int>(type: "int", nullable: true),
                    ValidaAteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitacoesCorreios", x => x.Id);
                    table.CheckConstraint("CK_SolicitacoesCorreios_Revisao", "([Revisao] IS NULL AND [ValidaAteUtc] IS NULL OR [Revisao] > 0 AND [ValidaAteUtc] IS NOT NULL) AND ([RevisaoAceita] IS NULL OR [RevisaoAceita] = [Revisao])");
                    table.CheckConstraint("CK_SolicitacoesCorreios_Situacao", "[Situacao] BETWEEN 1 AND 7 AND [Versao] > 0");
                });

            migrationBuilder.CreateTable(
                name: "OperacoesCotacoesCorreios",
                columns: table => new
                {
                    Autor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Admin = table.Column<bool>(type: "bit", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SolicitacaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Impressao = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Acao = table.Column<short>(type: "smallint", nullable: false),
                    ExecutadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ResultadoJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesCotacoesCorreios", x => new { x.Autor, x.Admin, x.Chave });
                    table.ForeignKey(
                        name: "FK_OperacoesCotacoesCorreios_SolicitacoesCorreios_SolicitacaoId",
                        column: x => x.SolicitacaoId,
                        principalTable: "SolicitacoesCorreios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropostasCorreios",
                columns: table => new
                {
                    SolicitacaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revisao = table.Column<int>(type: "int", nullable: false),
                    Servico = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FreteCentavos = table.Column<long>(type: "bigint", nullable: false),
                    PrazoDias = table.Column<int>(type: "int", nullable: false),
                    ValidaAteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PesoTotalGramas = table.Column<int>(type: "int", nullable: true),
                    VolumesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublicadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublicadaPor = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropostasCorreios", x => new { x.SolicitacaoId, x.Revisao });
                    table.CheckConstraint("CK_PropostasCorreios_Valores", "[Revisao] > 0 AND [FreteCentavos] >= 0 AND [PrazoDias] > 0 AND [ValidaAteUtc] > [PublicadaEmUtc]");
                    table.ForeignKey(
                        name: "FK_PropostasCorreios_SolicitacoesCorreios_SolicitacaoId",
                        column: x => x.SolicitacaoId,
                        principalTable: "SolicitacoesCorreios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConfiguracoesAtendimento_ValidadePostal",
                table: "ConfiguracoesAtendimento",
                sql: "[ValidadePropostaCorreiosHoras] BETWEEN 1 AND 168");

            migrationBuilder.CreateIndex(
                name: "IX_OperacoesCotacoesCorreios_SolicitacaoId",
                table: "OperacoesCotacoesCorreios",
                column: "SolicitacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesCorreios_ClienteId_CriadaEmUtc",
                table: "SolicitacoesCorreios",
                columns: new[] { "ClienteId", "CriadaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesCorreios_Situacao_CriadaEmUtc",
                table: "SolicitacoesCorreios",
                columns: new[] { "Situacao", "CriadaEmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacoesCotacoesCorreios");

            migrationBuilder.DropTable(
                name: "PropostasCorreios");

            migrationBuilder.DropTable(
                name: "SolicitacoesCorreios");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ConfiguracoesAtendimento_ValidadePostal",
                table: "ConfiguracoesAtendimento");

            migrationBuilder.DropColumn(
                name: "CotacaoManualCorreiosHabilitada",
                table: "ConfiguracoesAtendimento");

            migrationBuilder.DropColumn(
                name: "ValidadePropostaCorreiosHoras",
                table: "ConfiguracoesAtendimento");
        }
    }
}
