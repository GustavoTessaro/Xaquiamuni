using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurarPixManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracoesPixManual",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    TipoChave = table.Column<int>(type: "int", nullable: false),
                    ChavePix = table.Column<string>(type: "nvarchar(77)", maxLength: 77, nullable: false),
                    NomeRecebedor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    InstrucoesAdicionais = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesPixManual", x => x.Id);
                    table.CheckConstraint("CK_ConfiguracoesPixManual_Singleton", "[Id] = 1");
                    table.CheckConstraint("CK_ConfiguracoesPixManual_Tipo", "[TipoChave] IN (1, 2, 3)");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracoesPixManual");
        }
    }
}
