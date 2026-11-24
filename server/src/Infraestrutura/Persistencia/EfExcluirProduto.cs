using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Imagens;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoExclusaoProduto(Guid Autor, Guid Chave, Guid ProdutoId, string Nome,
    string VariantesJson, string Versao, DateTimeOffset InstanteUtc, string Resultado);

public sealed class EfExcluirProduto(XaquiamuniDbContext db, TimeProvider relogio,
    ILogger<EfExcluirProduto>? logger = null) : IExcluirProduto
{
    public async Task<(int Status, string? Codigo)> ExecutarAsync(Guid produtoId, string versao, Guid autor, Guid chave,
        Func<Produto, string> calcularVersao, CancellationToken ct)
    {
        try
        {
            await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await DependenciasExclusaoCatalogo.BloquearAsync(db, $"product-delete-key:{autor:D}:{chave:D}", ct);
            var auditoria = db.Set<OperacaoExclusaoProduto>();
            var anterior = await auditoria.FromSqlInterpolated($"""
                SELECT * FROM [OperacoesExclusaoProduto] WITH (READCOMMITTEDLOCK)
                WHERE [Autor] = {autor} AND [Chave] = {chave}
                """).AsNoTracking().SingleOrDefaultAsync(ct);
            if (anterior is not null)
                return anterior.ProdutoId == produtoId && anterior.Versao == versao ? (204, null) : (409, "chave_em_uso");

            // Mantém a ordem de checkout e de operações com múltiplas variantes.
            await PromocoesComerciaisSql.BloquearAsync(db, escrita: true, ct);
            var variantes = await db.Database.SqlQuery<Guid>($"""
                SELECT Id AS [Value] FROM ProdutoVariantes WITH (READCOMMITTEDLOCK) WHERE ProdutoId = {produtoId}
                """).ToArrayAsync(ct);
            Array.Sort(variantes);
            foreach (var variante in variantes)
                await DependenciasExclusaoCatalogo.BloquearAsync(db, $"finished-variant:{variante:D}", ct);

            var produto = await EfProdutoRepository.IncluirAgregado(db.Produtos).SingleOrDefaultAsync(p => p.Id == produtoId, ct);
            if (produto is null) return (404, "produto_nao_encontrado");
            if (!variantes.SequenceEqual(produto.Variantes.Select(v => v.Id).Order()) || calcularVersao(produto) != versao)
                return (409, "versao_desatualizada");

            var dependencia = await new DependenciasExclusaoCatalogo(db).ConsultarAsync(produtoId, null, ct);
            if (dependencia is not null) return (409, dependencia);
            if (produto.Imagens.Any(i => !ChaveArmazenamentoImagemProduto.EhValida(i.ChaveArmazenamento)))
                return (409, "produto_imagem_inconclusiva");

            var agora = relogio.GetUtcNow();
            var quantidadeImagens = produto.Imagens.Count;
            foreach (var imagem in produto.Imagens)
                db.Add(new LimpezaImagemProduto(Guid.NewGuid(), imagem.ChaveArmazenamento, agora));

            db.RemoveRange(produto.Imagens.SelectMany(i => i.Variantes).ToArray());
            db.RemoveRange(produto.Variantes.SelectMany(v => v.CaracteristicaValores).ToArray());
            db.RemoveRange(produto.Imagens);
            db.RemoveRange(produto.Caracteristicas.SelectMany(c => c.Valores).ToArray());
            db.RemoveRange(produto.Caracteristicas);
            db.RemoveRange(produto.Variantes);
            db.Remove(produto);
            auditoria.Add(new(autor, chave, produtoId, produto.Nome, JsonSerializer.Serialize(variantes), versao, agora, "Excluido"));

            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            if (quantidadeImagens > 0)
                logger?.LogInformation("{Quantidade} intenções de limpeza de imagens registradas para o produto {ProdutoId}.",
                    quantidadeImagens, produtoId);
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
