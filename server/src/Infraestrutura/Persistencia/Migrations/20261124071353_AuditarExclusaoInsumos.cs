using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xaquiamuni.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AuditarExclusaoInsumos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenciasUsoInsumos",
                columns: table => new
                {
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormaCompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Origem = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Comprovada = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenciasUsoInsumos", x => new { x.InsumoId, x.FormaCompraId, x.Origem });
                });

            migrationBuilder.CreateTable(
                name: "OperacoesExclusaoInsumo",
                columns: table => new
                {
                    Autor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormaCompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FormasJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Versao = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    InstanteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesExclusaoInsumo", x => new { x.Autor, x.Chave });
                });

            migrationBuilder.Sql("""
                DECLARE @semForma uniqueidentifier = '00000000-0000-0000-0000-000000000000';

                INSERT INTO EvidenciasUsoInsumos (InsumoId, FormaCompraId, Origem, Comprovada)
                SELECT InsumoId, @semForma, N'Compra', 1 FROM ComponentesCompraMateriaPrima
                UNION
                SELECT InsumoId, ApresentacaoId, N'Compra', 1 FROM ComponentesCompraMateriaPrima
                WHERE ApresentacaoId IS NOT NULL;

                -- Rascunhos anteriores podiam substituir componentes sem guardar os identificadores.
                -- O hash de criação não permite reconstruir os componentes removidos.
                IF EXISTS (SELECT 1 FROM ComprasMateriasPrimas)
                BEGIN
                    INSERT INTO EvidenciasUsoInsumos (InsumoId, FormaCompraId, Origem, Comprovada)
                    SELECT Id, @semForma, N'Compra', 0 FROM InsumosEstoque AS i
                    WHERE NOT EXISTS (SELECT 1 FROM EvidenciasUsoInsumos AS u
                        WHERE u.InsumoId = i.Id AND u.FormaCompraId = @semForma AND u.Origem = N'Compra')
                    UNION ALL
                    SELECT InsumoId, Id, N'Compra', 0 FROM FormasCompraInsumo AS f
                    WHERE NOT EXISTS (SELECT 1 FROM EvidenciasUsoInsumos AS u
                        WHERE u.InsumoId = f.InsumoId AND u.FormaCompraId = f.Id AND u.Origem = N'Compra');
                END;

                -- Os marcadores antigos de contagem possuem apenas o fingerprint da requisição.
                IF EXISTS (SELECT 1 FROM AjustesSemDiferencaInsumo)
                    INSERT INTO EvidenciasUsoInsumos (InsumoId, FormaCompraId, Origem, Comprovada)
                    SELECT Id, @semForma, N'Contagem', 0 FROM InsumosEstoque;
                """);

            migrationBuilder.Sql("""
                EXEC(N'
                CREATE TRIGGER TR_ComponentesCompraMateriaPrima_RegistrarUso
                ON ComponentesCompraMateriaPrima AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DECLARE @semForma uniqueidentifier = ''00000000-0000-0000-0000-000000000000'';
                    WITH componentes AS (
                        SELECT InsumoId, ApresentacaoId FROM inserted
                        UNION
                        SELECT InsumoId, ApresentacaoId FROM deleted
                    ), referencias AS (
                        SELECT InsumoId, @semForma AS FormaCompraId FROM componentes
                        UNION
                        SELECT InsumoId, ApresentacaoId FROM componentes WHERE ApresentacaoId IS NOT NULL
                    )
                    MERGE EvidenciasUsoInsumos WITH (HOLDLOCK) AS alvo
                    USING referencias AS origem
                    ON alvo.InsumoId = origem.InsumoId AND alvo.FormaCompraId = origem.FormaCompraId AND alvo.Origem = N''Compra''
                    WHEN MATCHED AND alvo.Comprovada = 0 THEN UPDATE SET Comprovada = 1
                    WHEN NOT MATCHED THEN INSERT (InsumoId, FormaCompraId, Origem, Comprovada)
                        VALUES (origem.InsumoId, origem.FormaCompraId, N''Compra'', 1);
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER TR_ComponentesCompraMateriaPrima_RegistrarUso;");
            migrationBuilder.DropTable(
                name: "EvidenciasUsoInsumos");

            migrationBuilder.DropTable(
                name: "OperacoesExclusaoInsumo");
        }
    }
}
