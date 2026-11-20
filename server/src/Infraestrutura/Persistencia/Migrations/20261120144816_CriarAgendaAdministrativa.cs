using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CriarAgendaAdministrativa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompromissosAgenda",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Versao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Categoria = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    HorarioInicial = table.Column<TimeOnly>(type: "time", nullable: true),
                    HorarioFinal = table.Column<TimeOnly>(type: "time", nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtualizadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExcluidoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExcluidoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompromissosAgenda", x => x.Id);
                    table.CheckConstraint("CK_CompromissosAgenda_Horarios", "[HorarioFinal] IS NULL OR ([HorarioInicial] IS NOT NULL AND [HorarioFinal] > [HorarioInicial])");
                });

            migrationBuilder.CreateTable(
                name: "OperacoesAgenda",
                columns: table => new
                {
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operacao = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Impressao = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesAgenda", x => new { x.ResponsavelId, x.Chave });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompromissosAgenda_Data_HorarioInicial_Id",
                table: "CompromissosAgenda",
                columns: new[] { "Data", "HorarioInicial", "Id" },
                filter: "[ExcluidoEmUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompromissosAgenda");

            migrationBuilder.DropTable(
                name: "OperacoesAgenda");
        }
    }
}
