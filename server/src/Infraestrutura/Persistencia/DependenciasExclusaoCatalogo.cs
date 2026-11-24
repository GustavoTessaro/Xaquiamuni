using Microsoft.EntityFrameworkCore;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed class DependenciasExclusaoCatalogo(XaquiamuniDbContext db)
{
    internal static Task BloquearAsync(XaquiamuniDbContext db, string recurso, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @resultado < 0 THROW 51000, 'Catalog lock unavailable.', 1;
            """, ct);

    internal async Task<string?> ConsultarAsync(Guid produto, Guid? variante, CancellationToken ct)
    {
        // O cadastro não cria lotes nem políticas de estoque. Saldos, reservas e movimentos
        // têm FK para o lote, que é preservado mesmo quando o saldo físico chega a zero.
        var vinculo = await db.Database.SqlQuery<int>($"""
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM ItensPedido WHERE ProdutoVarianteId IN (SELECT Id FROM [ProdutoVariantes] WHERE ProdutoId = {produto} AND ({variante} IS NULL OR Id = {variante})))
                  OR EXISTS (SELECT 1 FROM ItensVendaExterna WHERE VarianteId IN (SELECT Id FROM [ProdutoVariantes] WHERE ProdutoId = {produto} AND ({variante} IS NULL OR Id = {variante})))
                  OR EXISTS (SELECT 1 FROM PromocoesVariantes WHERE VarianteId IN (SELECT Id FROM [ProdutoVariantes] WHERE ProdutoId = {produto} AND ({variante} IS NULL OR Id = {variante})))
                  OR ({variante} IS NOT NULL AND EXISTS (SELECT 1 FROM Promocoes WHERE ProdutoId = {produto} AND Escopo = 'TodasVariantes'))
                  THEN 1
                WHEN ({variante} IS NULL AND (EXISTS (SELECT 1 FROM ItensPedido WHERE ProdutoId = {produto})
                    OR EXISTS (SELECT 1 FROM ItensVendaExterna WHERE ProdutoId = {produto})
                    OR EXISTS (SELECT 1 FROM Promocoes WHERE ProdutoId = {produto}))) THEN 4
                WHEN EXISTS (SELECT 1 FROM LotesProdutoAcabado WHERE ProdutoVarianteId IN (SELECT Id FROM [ProdutoVariantes] WHERE ProdutoId = {produto} AND ({variante} IS NULL OR Id = {variante})))
                  OR EXISTS (SELECT 1 FROM PoliticasEstoqueProdutoVariante WHERE ProdutoVarianteId IN (SELECT Id FROM [ProdutoVariantes] WHERE ProdutoId = {produto} AND ({variante} IS NULL OR Id = {variante})))
                  THEN 2
                WHEN EXISTS (SELECT 1 FROM ReceitasProducao WHERE ProdutoVarianteId IN (SELECT Id FROM [ProdutoVariantes] WHERE ProdutoId = {produto} AND ({variante} IS NULL OR Id = {variante})))
                  OR EXISTS (SELECT 1 FROM OrdensProducao WHERE ProdutoVarianteId IN (SELECT Id FROM [ProdutoVariantes] WHERE ProdutoId = {produto} AND ({variante} IS NULL OR Id = {variante})))
                  THEN 3
                ELSE 0 END AS [Value]
            """).SingleAsync(ct);
        if (vinculo != 0) return vinculo switch
        {
            1 => "variante_com_historico_comercial",
            4 => "produto_com_historico_comercial",
            2 => "variante_com_historico_estoque",
            _ => "variante_com_historico_producao"
        };

        var historico = await ConsultarSnapshotsAsync(produto, variante, ct);
        return historico switch
        {
            3 => "produto_com_historico_comercial",
            2 => "variante_com_historico_comercial",
            1 => variante.HasValue ? "variante_historico_inconclusivo" : "produto_historico_inconclusivo",
            _ => null
        };
    }

    private Task<int> ConsultarSnapshotsAsync(Guid produto, Guid? variante, CancellationToken ct) =>
        db.Database.SqlQuery<int>($$"""
            SELECT COALESCE(MAX(historico.Resultado), 0) AS [Value]
            FROM (
                SELECT CASE WHEN {{variante}} IS NULL AND campos.ProdutoEncontrado = 1 THEN 3
                    WHEN formato.Valido = 0 OR item.[type] IS NULL OR item.[type] <> 5
                    OR campos.Total <> 1 OR campos.Invalido = 1 OR ({{variante}} IS NULL AND campos.Produtos <> 1)
                    OR TRY_CONVERT(uniqueidentifier, produto.Id) IS NULL OR DATALENGTH(produto.Id) <> 72 THEN 1
                    WHEN campos.Encontrado = 1 THEN 2 ELSE 0 END AS Resultado
                FROM SolicitacoesCorreios AS c
                CROSS APPLY (VALUES (CASE WHEN ISJSON(c.ItensJson) = 1 AND
                    LEFT(LTRIM(REPLACE(REPLACE(REPLACE(c.ItensJson, CHAR(9), ' '), CHAR(10), ' '), CHAR(13), ' ')), 1) = '['
                    THEN 1 ELSE 0 END)) AS formato(Valido)
                OUTER APPLY OPENJSON(CASE WHEN formato.Valido = 1 THEN c.ItensJson ELSE N'[]' END) AS item
                CROSS APPLY (VALUES (COALESCE(
                    JSON_VALUE(CASE WHEN item.[type] = 5 THEN item.[value] ELSE N'{}' END, '$.produtoId'),
                    JSON_VALUE(CASE WHEN item.[type] = 5 THEN item.[value] ELSE N'{}' END, '$.ProdutoId')
                ))) AS produto(Id)
                CROSS APPLY (
                    SELECT COUNT(CASE WHEN tipo.Variante = 1 THEN 1 END) AS Total,
                        COUNT(CASE WHEN tipo.Variante = 0 THEN 1 END) AS Produtos,
                        MAX(CASE WHEN tipo.Variante = 0 AND campo.[type] = 1 AND DATALENGTH(campo.[value]) = 72
                            AND TRY_CONVERT(uniqueidentifier, campo.[value]) = {{produto}} THEN 1 ELSE 0 END) AS ProdutoEncontrado,
                        MAX(CASE WHEN tipo.Variante = 1 AND campo.[type] = 1 AND DATALENGTH(campo.[value]) = 72
                            AND v.Id IS NOT NULL THEN 1 ELSE 0 END) AS Encontrado,
                        MAX(CASE WHEN tipo.Variante = 0 OR (campo.[type] = 1 AND DATALENGTH(campo.[value]) = 72
                            AND TRY_CONVERT(uniqueidentifier, campo.[value]) IS NOT NULL) THEN 0 ELSE 1 END) AS Invalido
                    FROM OPENJSON(CASE WHEN item.[type] = 5 THEN item.[value] ELSE N'{}' END) AS campo
                    CROSS APPLY (VALUES (CASE WHEN campo.[key] IN (N'produtoVarianteId', N'ProdutoVarianteId')
                        THEN 1 ELSE 0 END)) AS tipo(Variante)
                    LEFT JOIN ProdutoVariantes AS v ON v.ProdutoId = {{produto}}
                        AND ({{variante}} IS NULL OR v.Id = {{variante}})
                        AND v.Id = TRY_CONVERT(uniqueidentifier, campo.[value])
                    WHERE campo.[key] IN (N'produtoId', N'ProdutoId', N'produtoVarianteId', N'ProdutoVarianteId')
                ) AS campos
                UNION ALL
                SELECT CASE
                    WHEN raiz.Detalhes <> 1 OR campos.Produto <> 1 OR campos.Lista <> 1 OR campos.Escopo <> 1
                        OR TRY_CONVERT(uniqueidentifier, ids.Produto) IS NULL OR DATALENGTH(ids.Produto) <> 72
                        OR ids.Lista IS NULL OR LEFT(LTRIM(ids.Lista), 1) <> '['
                        OR COALESCE(ids.Escopo, '') NOT IN (N'TodasVariantes', N'VariantesSelecionadas') THEN 1
                    WHEN {{variante}} IS NULL THEN 3
                    WHEN ids.Escopo = N'TodasVariantes' THEN 2
                    WHEN item.[type] IS NULL THEN 1
                    WHEN item.[type] <> 1 OR DATALENGTH(item.[value]) <> 72 OR
                        TRY_CONVERT(uniqueidentifier, item.[value]) IS NULL THEN 1
                    WHEN TRY_CONVERT(uniqueidentifier, item.[value]) = {{variante}} THEN 2
                    ELSE 0 END
                FROM OperacoesPromocao AS o
                CROSS APPLY (VALUES (CASE WHEN ISJSON(o.Resultado) = 1 THEN o.Resultado ELSE N'{}' END)) AS original(JsonSeguro)
                CROSS APPLY (
                    SELECT COUNT(*) AS Detalhes
                    FROM OPENJSON(original.JsonSeguro) WHERE [key] = N'Detalhe'
                ) AS raiz
                CROSS APPLY (
                    SELECT COUNT(CASE WHEN [key] = N'ProdutoId' THEN 1 END) AS Produto,
                        COUNT(CASE WHEN [key] = N'VariantesIds' THEN 1 END) AS Lista,
                        COUNT(CASE WHEN [key] = N'Escopo' THEN 1 END) AS Escopo
                    FROM OPENJSON(COALESCE(JSON_QUERY(original.JsonSeguro, '$.Detalhe'), N'{}'))
                ) AS campos
                CROSS APPLY (VALUES (JSON_QUERY(original.JsonSeguro, '$.Detalhe.VariantesIds'),
                    JSON_VALUE(original.JsonSeguro, '$.Detalhe.ProdutoId'),
                    JSON_VALUE(original.JsonSeguro, '$.Detalhe.Escopo'))) AS ids(Lista, Produto, Escopo)
                OUTER APPLY OPENJSON(CASE WHEN LEFT(LTRIM(ids.Lista), 1) = '[' THEN ids.Lista ELSE N'[]' END) AS item
                WHERE TRY_CONVERT(uniqueidentifier, ids.Produto) = {{produto}}
                    OR raiz.Detalhes <> 1 OR campos.Produto <> 1
                    OR ids.Produto IS NULL OR DATALENGTH(ids.Produto) <> 72
                    OR TRY_CONVERT(uniqueidentifier, ids.Produto) IS NULL
            ) AS historico
            """).SingleAsync(ct);
}
