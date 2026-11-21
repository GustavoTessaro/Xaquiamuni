using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarPublicacaoTemaLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracoesTemaLoja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Chave = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revisao = table.Column<int>(type: "int", nullable: false),
                    PublicadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AdministradorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UltimaChaveIdempotencia = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesTemaLoja", x => x.Id);
                    table.CheckConstraint("CK_ConfiguracoesTemaLoja_Unica", "[Id] = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracoesTemaLoja");
        }
    }
}
