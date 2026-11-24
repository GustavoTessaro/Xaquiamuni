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
            await DependenciasExclusaoCatalogo.BloquearAsync(db, $"variant-delete-key:{autor:D}:{chave:D}", ct);
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
            await DependenciasExclusaoCatalogo.BloquearAsync(db, $"finished-variant:{varianteId:D}", ct);
            var produto = await EfProdutoRepository.IncluirAgregado(db.Produtos).SingleOrDefaultAsync(p => p.Id == produtoId, ct);
            var variante = produto?.Variantes.SingleOrDefault(v => v.Id == varianteId);
            if (produto is null || variante is null) return (404, "variante_nao_encontrada");
            if (calcularVersao(produto) != versao) return (409, "versao_desatualizada");
            if (produto.Variantes.Count == 1) return (409, "ultima_variante");
            if (variante.Padrao || !produto.Variantes.Any(v => v.Ativa && v.Padrao))
                return (409, "variante_padrao");

            var dependencia = await new DependenciasExclusaoCatalogo(db).ConsultarAsync(produtoId, varianteId, ct);
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

}
