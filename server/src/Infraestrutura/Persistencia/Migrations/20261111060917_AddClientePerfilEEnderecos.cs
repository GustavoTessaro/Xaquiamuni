using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddClientePerfilEEnderecos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientePerfis",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeCompleto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CpfCiphertext = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CpfNonce = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CpfAuthenticationTag = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CpfFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CpfEncryptionKeyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CpfFingerprintKeyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientePerfis", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_ClientePerfis_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClienteEnderecos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cep = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Uf = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Cidade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Bairro = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Logradouro = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Complemento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClienteEnderecos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClienteEnderecos_ClientePerfis_ClienteUserId",
                        column: x => x.ClienteUserId,
                        principalTable: "ClientePerfis",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClienteEnderecos_ClienteUserId",
                table: "ClienteEnderecos",
                column: "ClienteUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ClienteEnderecos_DefaultPerClient",
                table: "ClienteEnderecos",
                column: "ClienteUserId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ClientePerfis_CpfFingerprint",
                table: "ClientePerfis",
                column: "CpfFingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClienteEnderecos");

            migrationBuilder.DropTable(
                name: "ClientePerfis");
        }
    }
}
