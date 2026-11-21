using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RegistrarPrecoAceitoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PrecoAceitoCentavos",
                table: "ItensPedido",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PrecoRegularCentavos",
                table: "ItensPedido",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromocaoId",
                table: "ItensPedido",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrecoAceitoCentavos",
                table: "ItensPedido");

            migrationBuilder.DropColumn(
                name: "PrecoRegularCentavos",
                table: "ItensPedido");

            migrationBuilder.DropColumn(
                name: "PromocaoId",
                table: "ItensPedido");
        }
    }
}
