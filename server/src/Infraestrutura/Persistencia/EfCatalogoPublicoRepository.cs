using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo.Publico;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfCatalogoPublicoRepository(XaquiamuniDbContext dbContext)
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

        return new CatalogoPublicoPagina(resultado, totalItens);
    }

    public Task<Produto?> ConsultarProdutoAsync(
        Guid produtoId,
        CancellationToken cancellationToken = default) =>
        IncluirDetalhesPublicos(FiltrarProdutosVisiveis(dbContext.Produtos.AsNoTracking()))
            .SingleOrDefaultAsync(produto => produto.Id == produtoId, cancellationToken);

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
