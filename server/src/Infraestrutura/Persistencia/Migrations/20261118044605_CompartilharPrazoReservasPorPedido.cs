using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CompartilharPrazoReservasPorPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT [PedidoId]
                    FROM [ReservasProdutoAcabado] WITH (TABLOCKX, HOLDLOCK)
                    GROUP BY [PedidoId]
                    HAVING MIN([ExpiraEmUtc]) <> MAX([ExpiraEmUtc])
                )
                    THROW 51001, 'Existem pedidos com prazos de reserva divergentes. Reconcilie antes de migrar.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReservasProdutoAcabado_Prazo_24Horas",
                table: "ReservasProdutoAcabado");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReservasProdutoAcabado_Prazo_Ate24Horas",
                table: "ReservasProdutoAcabado",
                sql: "[ExpiraEmUtc] > [CriadaEmUtc] AND (DATEDIFF_BIG(hour, [CriadaEmUtc], [ExpiraEmUtc]) < 24 OR (DATEDIFF_BIG(hour, [CriadaEmUtc], [ExpiraEmUtc]) = 24 AND CAST([ExpiraEmUtc] AS time(7)) <= CAST([CriadaEmUtc] AS time(7))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ReservasProdutoAcabado_Prazo_Ate24Horas",
                table: "ReservasProdutoAcabado");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReservasProdutoAcabado_Prazo_24Horas",
                table: "ReservasProdutoAcabado",
                sql: "DATEDIFF_BIG(hour, [CriadaEmUtc], [ExpiraEmUtc]) = 24 AND CAST([CriadaEmUtc] AS time(7)) = CAST([ExpiraEmUtc] AS time(7))");
        }
    }
}
