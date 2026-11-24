using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoExclusaoVariante(Guid Autor, Guid Chave, Guid ProdutoId, Guid VarianteId,
    string Identificacao, string Versao, DateTimeOffset InstanteUtc, string Resultado);

public sealed class EfExcluirVariante(XaquiamuniDbContext db, TimeProvider relogio) : IExcluirVariante
{
    public async Task<(int Status, string? Codigo)> ExecutarAsync(Guid produtoId, Guid varianteId, string versao,
        Guid autor, Guid chave, Func<Produto, string> calcularVersao, CancellationToken ct)
    {
        try
        {
            await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await BloquearAsync($"variant-delete-key:{autor:D}:{chave:D}", ct);
            var auditoria = db.Set<OperacaoExclusaoVariante>();
            var anterior = await auditoria.FromSqlInterpolated($"""
                SELECT * FROM [OperacoesExclusaoVariante] WITH (READCOMMITTEDLOCK)
                WHERE [Autor] = {autor} AND [Chave] = {chave}
                """).AsNoTracking().SingleOrDefaultAsync(ct);
            if (anterior is not null)
                return anterior.ProdutoId == produtoId && anterior.VarianteId == varianteId && anterior.Versao == versao
                    ? (204, null) : (409, "chave_em_uso");

            // Checkout adquire promoções antes da variante. A cotação postal também usa o lock da variante.
            await PromocoesComerciaisSql.BloquearAsync(db, escrita: true, ct);
            await BloquearAsync($"finished-variant:{varianteId:D}", ct);
            var produto = await EfProdutoRepository.IncluirAgregado(db.Produtos).SingleOrDefaultAsync(p => p.Id == produtoId, ct);
            var variante = produto?.Variantes.SingleOrDefault(v => v.Id == varianteId);
            if (produto is null || variante is null) return (404, "variante_nao_encontrada");
            if (calcularVersao(produto) != versao) return (409, "versao_desatualizada");
            if (produto.Variantes.Count == 1) return (409, "ultima_variante");
            if (variante.Padrao || !produto.Variantes.Any(v => v.Ativa && v.Padrao))
                return (409, "variante_padrao");

            var dependencia = await ConsultarDependenciaAsync(produtoId, varianteId, ct);
            if (dependencia is not null) return (409, dependencia);

            db.RemoveRange(produto.Imagens.SelectMany(i => i.Variantes).Where(a => a.ProdutoVarianteId == varianteId).ToArray());
            db.RemoveRange(variante.CaracteristicaValores);
            db.Remove(variante);
            auditoria.Add(new(autor, chave, produtoId, varianteId, variante.Identificacao, versao, relogio.GetUtcNow(), "Excluida"));
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return (204, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (409, "versao_desatualizada");
        }
        catch (Exception e) when (e is SqlException { Number: 1205 or 1222 or 51000 }
            or DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 } })
        {
            return (409, "conflito_concorrencia");
        }
    }

    private Task BloquearAsync(string recurso, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @resultado < 0 THROW 51000, 'Variant lock unavailable.', 1;
            """, ct);

    private async Task<string?> ConsultarDependenciaAsync(Guid produto, Guid variante, CancellationToken ct)
    {
        // O cadastro não cria lotes nem políticas de estoque. Saldos, reservas e movimentos
        // têm FK para o lote, que é preservado mesmo quando o saldo físico chega a zero.
        var vinculo = await db.Database.SqlQuery<int>($"""
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM ItensPedido WHERE ProdutoVarianteId = {variante})
                  OR EXISTS (SELECT 1 FROM ItensVendaExterna WHERE VarianteId = {variante})
                  OR EXISTS (SELECT 1 FROM PromocoesVariantes WHERE VarianteId = {variante})
                  OR EXISTS (SELECT 1 FROM Promocoes WHERE ProdutoId = {produto} AND Escopo = 'TodasVariantes')
                  THEN 1
                WHEN EXISTS (SELECT 1 FROM LotesProdutoAcabado WHERE ProdutoVarianteId = {variante})
                  OR EXISTS (SELECT 1 FROM PoliticasEstoqueProdutoVariante WHERE ProdutoVarianteId = {variante})
                  THEN 2
                WHEN EXISTS (SELECT 1 FROM ReceitasProducao WHERE ProdutoVarianteId = {variante})
                  OR EXISTS (SELECT 1 FROM OrdensProducao WHERE ProdutoVarianteId = {variante})
                  THEN 3
                ELSE 0 END AS [Value]
            """).SingleAsync(ct);
        if (vinculo != 0) return vinculo switch
        {
            1 => "variante_com_historico_comercial",
            2 => "variante_com_historico_estoque",
            _ => "variante_com_historico_producao"
        };

        var historico = await ConsultarSnapshotsAsync(produto, variante, ct);
        return historico switch
        {
            2 => "variante_com_historico_comercial",
            1 => "variante_historico_inconclusivo",
            _ => null
        };
    }

    private Task<int> ConsultarSnapshotsAsync(Guid produto, Guid variante, CancellationToken ct) =>
        db.Database.SqlQuery<int>($$"""
            SELECT COALESCE(MAX(historico.Resultado), 0) AS [Value]
            FROM (
                SELECT CASE WHEN formato.Valido = 0 OR item.[type] IS NULL OR item.[type] <> 5
                    OR campos.Total <> 1 OR campos.Invalido = 1
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
                    SELECT COUNT(*) AS Total,
                        MAX(CASE WHEN campo.[type] = 1 AND DATALENGTH(campo.[value]) = 72
                            AND TRY_CONVERT(uniqueidentifier, campo.[value]) = {{variante}} THEN 1 ELSE 0 END) AS Encontrado,
                        MAX(CASE WHEN campo.[type] = 1 AND DATALENGTH(campo.[value]) = 72
                            AND TRY_CONVERT(uniqueidentifier, campo.[value]) IS NOT NULL THEN 0 ELSE 1 END) AS Invalido
                    FROM OPENJSON(CASE WHEN item.[type] = 5 THEN item.[value] ELSE N'{}' END) AS campo
                    WHERE campo.[key] IN (N'produtoVarianteId', N'ProdutoVarianteId')
                ) AS campos
                UNION ALL
                SELECT CASE
                    WHEN raiz.Detalhes <> 1 OR campos.Produto <> 1 OR campos.Lista <> 1 OR campos.Escopo <> 1
                        OR TRY_CONVERT(uniqueidentifier, ids.Produto) IS NULL OR DATALENGTH(ids.Produto) <> 72
                        OR ids.Lista IS NULL OR LEFT(LTRIM(ids.Lista), 1) <> '['
                        OR COALESCE(ids.Escopo, '') NOT IN (N'TodasVariantes', N'VariantesSelecionadas') THEN 1
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
