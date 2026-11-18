using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo.Publico;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfCatalogoPublicoRepository(
    XaquiamuniDbContext dbContext, ICalendarioOperacionalEstoque calendario)
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

        var disponiveis = await ConsultarOpcoesDisponiveisAsync(resultado, cancellationToken).ConfigureAwait(false);
        return new CatalogoPublicoPagina(resultado.Select(produto => Mapear(produto, disponiveis)).ToArray(), totalItens);
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

        var disponiveis = await ConsultarOpcoesDisponiveisAsync([produto], cancellationToken).ConfigureAwait(false);
        return Mapear(produto, disponiveis);
    }

    private async Task<HashSet<Guid>> ConsultarOpcoesDisponiveisAsync(
        IReadOnlyList<Produto> produtos, CancellationToken cancellationToken)
    {
        if (produtos.Count == 0)
        {
            return [];
        }

        var variantes = produtos.SelectMany(produto => produto.Variantes).Select(variante => variante.Id).ToArray();
        var agoraUtc = await dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        var dataOperacional = calendario.ObterDataEm(agoraUtc);
        var compromissos = dbContext.ReservasProdutoAcabado.ComprometidasEm(agoraUtc);
        var disponiveis = await (
            from lote in dbContext.LotesProdutoAcabado
            join saldo in dbContext.SaldosProdutoAcabado on new { lote.ProdutoVarianteId, LoteId = lote.Id }
                equals new { saldo.ProdutoVarianteId, saldo.LoteId }
            where variantes.Contains(lote.ProdutoVarianteId) && lote.DataValidade >= dataOperacional
                && dbContext.ProdutoVariantes.Any(variante => variante.Id == lote.ProdutoVarianteId && variante.Ativa)
                && saldo.QuantidadeVendavel > 0
                && saldo.QuantidadeVendavel > compromissos
                    .Where(reserva => reserva.ProdutoVarianteId == lote.ProdutoVarianteId && reserva.LoteId == lote.Id)
                    .Sum(reserva => (long)reserva.Quantidade)
            select lote.ProdutoVarianteId
        ).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        return disponiveis.ToHashSet();
    }

    private static ProdutoCatalogoPublico Mapear(Produto produto, HashSet<Guid> disponiveis) =>
        new(produto, produto.Variantes.Where(variante => disponiveis.Contains(variante.Id))
            .Select(variante => variante.Id).ToHashSet());

    private static IQueryable<Produto> FiltrarProdutosVisiveis(IQueryable<Produto> produtos) =>
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
