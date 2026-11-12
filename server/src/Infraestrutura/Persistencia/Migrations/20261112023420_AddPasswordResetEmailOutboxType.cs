using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetEmailOutboxType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmailOutbox_MessageType_Valid",
                table: "EmailOutbox");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmailOutbox_MessageType_Valid",
                table: "EmailOutbox",
                sql: "[MessageType] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmailOutbox_MessageType_Valid",
                table: "EmailOutbox");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmailOutbox_MessageType_Valid",
                table: "EmailOutbox",
                sql: "[MessageType] IN (1)");
        }
    }
}
