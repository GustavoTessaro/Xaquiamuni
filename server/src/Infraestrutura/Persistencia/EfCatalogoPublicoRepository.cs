using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Catalogo.Publico;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfCatalogoPublicoRepository(
    XaquiamuniDbContext dbContext, ICalendarioOperacionalEstoque calendario,
    IOptions<PromocoesComerciaisOptions>? promocoes = null)
    : ICatalogoPublicoRepository
{
    public async Task<IReadOnlyList<Categoria>> ListarCategoriasAtivasAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Categorias
            .AsNoTracking()
            .Where(categoria => categoria.Ativa)
            .OrderBy(categoria => categoria.OrdemExibicao)
            .ThenBy(categoria => categoria.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<CatalogoPublicoPagina> ListarProdutosAsync(
        CatalogoPublicoFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var produtos = FiltrarProdutosVisiveis(dbContext.Produtos.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            produtos = produtos.Where(produto => produto.Nome.Contains(filtro.Busca));
        }

        if (filtro.CategoriaId is not null)
        {
            produtos = produtos.Where(produto => produto.CategoriaId == filtro.CategoriaId.Value);
        }

        var totalItens = await produtos.CountAsync(cancellationToken).ConfigureAwait(false);
        var resultado = await IncluirResumoPublico(produtos)
            .OrderBy(produto => produto.Nome)
            .ThenBy(produto => produto.Id)
            .Skip(filtro.Skip)
            .Take(filtro.Take)
            .AsSplitQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var publicos = await MapearAsync(resultado, cancellationToken).ConfigureAwait(false);
        return new CatalogoPublicoPagina(publicos, totalItens);
    }

    public async Task<ProdutoCatalogoPublico?> ConsultarProdutoAsync(
        Guid produtoId,
        CancellationToken cancellationToken = default)
    {
        var produto = await IncluirDetalhesPublicos(FiltrarProdutosVisiveis(dbContext.Produtos.AsNoTracking()))
            .SingleOrDefaultAsync(item => item.Id == produtoId, cancellationToken).ConfigureAwait(false);
        if (produto is null)
        {
            return null;
        }

        return (await MapearAsync([produto], cancellationToken).ConfigureAwait(false))[0];
    }

    private async Task<ProdutoCatalogoPublico[]> MapearAsync(
        IReadOnlyList<Produto> produtos, CancellationToken cancellationToken)
    {
        if (produtos.Count == 0)
        {
            return [];
        }

        var variantes = produtos.SelectMany(produto => produto.Variantes).Select(variante => variante.Id).ToArray();
        var agoraUtc = await dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        var dataOperacional = calendario.ObterDataEm(agoraUtc);
        var disponiveis = await VariantesDisponiveis(dbContext, agoraUtc, dataOperacional)
            .Where(id => variantes.Contains(id)).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var idsDisponiveis = disponiveis.ToHashSet();
        var vigentes = promocoes?.Value.Habilitadas == true
            ? await PromocoesComerciaisSql.CarregarAsync(dbContext, produtos.Select(p => p.Id).ToArray(),
                agoraUtc, cancellationToken) : null;
        return produtos.Select(p => new ProdutoCatalogoPublico(p,
            p.Variantes.Where(v => idsDisponiveis.Contains(v.Id)).Select(v => v.Id).ToHashSet(),
            vigentes is null ? null : p.Variantes.ToDictionary(v => v.Id,
                v => PromocoesComerciaisSql.Resolver(vigentes[p.Id], v.Id, v.PrecoVenda, agoraUtc)))).ToArray();
    }

    internal static IQueryable<Guid> VariantesDisponiveis(
        XaquiamuniDbContext dbContext, DateTimeOffset agoraUtc, DateOnly dataOperacional)
    {
        var compromissos = dbContext.ReservasProdutoAcabado.ComprometidasEm(agoraUtc);
        return
            from lote in dbContext.LotesProdutoAcabado
            join saldo in dbContext.SaldosProdutoAcabado on new { lote.ProdutoVarianteId, LoteId = lote.Id }
                equals new { saldo.ProdutoVarianteId, saldo.LoteId }
            where (lote.DataValidade == null || lote.DataValidade >= dataOperacional)
                && dbContext.ProdutoVariantes.Any(variante => variante.Id == lote.ProdutoVarianteId && variante.Ativa)
                && saldo.QuantidadeVendavel > 0
                && saldo.QuantidadeVendavel > compromissos
                    .Where(reserva => reserva.ProdutoVarianteId == lote.ProdutoVarianteId && reserva.LoteId == lote.Id)
                    .Sum(reserva => (long)reserva.Quantidade)
            select lote.ProdutoVarianteId;
    }

    internal static IQueryable<Produto> FiltrarProdutosVisiveis(IQueryable<Produto> produtos) =>
        produtos.Where(produto =>
            produto.Publicado
            && produto.Categoria.Ativa
            && produto.Variantes.Any(variante => variante.Ativa && variante.Padrao)
            && produto.Imagens.Any(imagem => imagem.Principal));

    private static IQueryable<Produto> IncluirResumoPublico(IQueryable<Produto> produtos) =>
        produtos
            .Include(produto => produto.Categoria)
            .Include(produto => produto.Variantes.Where(variante => variante.Ativa))
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Caracteristica)
            .Include(produto => produto.Variantes.Where(variante => variante.Ativa))
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Valor)
            .Include(produto => produto.Imagens.Where(imagem => imagem.Principal));

    private static IQueryable<Produto> IncluirDetalhesPublicos(IQueryable<Produto> produtos) =>
        produtos
            .Include(produto => produto.Categoria)
            .Include(produto => produto.Variantes.Where(variante => variante.Ativa))
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Caracteristica)
            .Include(produto => produto.Variantes.Where(variante => variante.Ativa))
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Valor)
            .Include(produto => produto.Caracteristicas)
                .ThenInclude(caracteristica => caracteristica.Valores)
            .Include(produto => produto.Imagens.OrderBy(imagem => imagem.OrdemExibicao))
                .ThenInclude(imagem => imagem.Variantes
                    .Where(associacao => associacao.Variante.Ativa))
            .AsSplitQuery();
}
