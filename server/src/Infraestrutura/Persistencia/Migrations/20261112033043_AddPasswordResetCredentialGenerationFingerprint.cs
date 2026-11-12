using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetCredentialGenerationFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CredentialGenerationFingerprint",
                table: "EmailOutbox",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmailOutbox_CredentialGenerationFingerprint_Valid",
                table: "EmailOutbox",
                sql: "([MessageType] = 1 AND [CredentialGenerationFingerprint] IS NULL) OR ([MessageType] = 2 AND [CredentialGenerationFingerprint] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[EmailOutbox]
                    WHERE [MessageType] = 2
                )
                BEGIN
                    THROW 51000, 'Cannot remove credential generation fingerprints while password reset messages exist.', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmailOutbox_CredentialGenerationFingerprint_Valid",
                table: "EmailOutbox");

            migrationBuilder.DropColumn(
                name: "CredentialGenerationFingerprint",
                table: "EmailOutbox");
        }
    }
}
