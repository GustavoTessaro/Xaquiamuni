using Xaquiamuni.Aplicacao.Catalogo.Imagens;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo.Publico;

public sealed record CatalogoPublicoFiltro(
    string? Busca,
    Guid? CategoriaId,
    int Skip,
    int Take);

public sealed record CatalogoPublicoPagina(
    IReadOnlyList<ProdutoCatalogoPublico> Produtos,
    int TotalItens);

public sealed record ProdutoCatalogoPublico(Produto Produto, IReadOnlySet<Guid> OpcoesDisponiveis,
    IReadOnlyDictionary<Guid, PrecoComercial>? Precos = null);

public interface ICatalogoPublicoRepository
{
    Task<IReadOnlyList<Categoria>> ListarCategoriasAtivasAsync(
        CancellationToken cancellationToken = default);

    Task<CatalogoPublicoPagina> ListarProdutosAsync(
        CatalogoPublicoFiltro filtro,
        CancellationToken cancellationToken = default);

    Task<ProdutoCatalogoPublico?> ConsultarProdutoAsync(
        Guid produtoId,
        CancellationToken cancellationToken = default);
}

public interface IConsultarCatalogoPublico
{
    Task<IReadOnlyList<Categoria>> ListarCategoriasAsync(
        CancellationToken cancellationToken = default);

    Task<CatalogoPublicoPagina> ListarProdutosAsync(
        CatalogoPublicoFiltro filtro,
        CancellationToken cancellationToken = default);

    Task<ProdutoCatalogoPublico?> ConsultarProdutoAsync(
        Guid produtoId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemConteudoResult> AbrirConteudoImagemAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default);
}

public sealed class ConsultarCatalogoPublico(
    ICatalogoPublicoRepository repository,
    IGerenciarImagensProduto gerenciarImagens) : IConsultarCatalogoPublico
{
    public Task<IReadOnlyList<Categoria>> ListarCategoriasAsync(
        CancellationToken cancellationToken = default) =>
        repository.ListarCategoriasAtivasAsync(cancellationToken);

    public Task<CatalogoPublicoPagina> ListarProdutosAsync(
        CatalogoPublicoFiltro filtro,
        CancellationToken cancellationToken = default) =>
        repository.ListarProdutosAsync(filtro, cancellationToken);

    public Task<ProdutoCatalogoPublico?> ConsultarProdutoAsync(
        Guid produtoId,
        CancellationToken cancellationToken = default) =>
        repository.ConsultarProdutoAsync(produtoId, cancellationToken);

    public Task<ProdutoImagemConteudoResult> AbrirConteudoImagemAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default) =>
        gerenciarImagens.AbrirConteudoPublicoAsync(produtoId, imagemId, cancellationToken);
}
