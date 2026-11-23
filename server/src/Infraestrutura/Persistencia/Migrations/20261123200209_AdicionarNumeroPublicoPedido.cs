using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarNumeroPublicoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NumeroPublico",
                table: "Pedidos",
                type: "varchar(13)",
                unicode: false,
                maxLength: 13,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_BackfillNumeroPublico",
                table: "Pedidos",
                column: "NumeroPublico",
                unique: true,
                filter: "[NumeroPublico] IS NOT NULL");

            migrationBuilder.Sql("""
                DECLARE @id uniqueidentifier, @codigo varchar(13), @bytes varbinary(10),
                    @pos int, @tentativas int, @indice int, @tamanho int, @limite int,
                    @alfabeto varchar(32) = '23456789ACDEFHJKMNPQRTUVWXY';
                SET @tamanho = LEN(@alfabeto);
                SET @limite = 256 - (256 % @tamanho);

                DECLARE pedidos_historicos CURSOR LOCAL STATIC READ_ONLY FOR
                    SELECT [Id] FROM [Pedidos] WHERE [NumeroPublico] IS NULL;
                OPEN pedidos_historicos;
                FETCH NEXT FROM pedidos_historicos INTO @id;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @tentativas = 0;
                    WHILE 1 = 1
                    BEGIN
                        SET @tentativas += 1;
                        IF @tentativas > 20
                            THROW 51000, 'Falha ao atribuir numero publico de pedido.', 1;

                        SET @bytes = CRYPT_GEN_RANDOM(10);
                        SET @codigo = 'XQ-';
                        SET @pos = 1;
                        WHILE @pos <= 10
                        BEGIN
                            SET @indice = CONVERT(int, SUBSTRING(@bytes, @pos, 1));
                            WHILE @indice >= @limite
                                SET @indice = CONVERT(int, CRYPT_GEN_RANDOM(1));
                            SET @codigo += SUBSTRING(@alfabeto, (@indice % @tamanho) + 1, 1);
                            SET @pos += 1;
                        END;

                        IF NOT EXISTS (SELECT 1 FROM [Pedidos] WHERE [NumeroPublico] = @codigo)
                        BEGIN
                            UPDATE [Pedidos] SET [NumeroPublico] = @codigo WHERE [Id] = @id;
                            BREAK;
                        END;
                    END;
                    FETCH NEXT FROM pedidos_historicos INTO @id;
                END;

                CLOSE pedidos_historicos;
                DEALLOCATE pedidos_historicos;
                """);

            migrationBuilder.DropIndex(name: "IX_Pedidos_BackfillNumeroPublico", table: "Pedidos");
            migrationBuilder.AlterColumn<string>(
                name: "NumeroPublico",
                table: "Pedidos",
                type: "varchar(13)",
                unicode: false,
                maxLength: 13,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(13)",
                oldUnicode: false,
                oldMaxLength: 13,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_NumeroPublico",
                table: "Pedidos",
                column: "NumeroPublico",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pedidos_NumeroPublico_Formato",
                table: "Pedidos",
                sql: "LEN([NumeroPublico]) = 13 AND LEFT([NumeroPublico], 3) COLLATE Latin1_General_100_BIN2 = 'XQ-' AND SUBSTRING([NumeroPublico], 4, 10) COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^23456789ACDEFHJKMNPQRTUVWXY]%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pedidos_NumeroPublico",
                table: "Pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pedidos_NumeroPublico_Formato",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "NumeroPublico",
                table: "Pedidos");
        }
    }
}
