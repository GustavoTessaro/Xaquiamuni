namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public interface IGerenciarImagensProduto
{
    Task<ProdutoImagemOperacaoResult> AdicionarAsync(
        Guid produtoId,
        AdicionarImagemProdutoInput? input,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemOperacaoResult> ListarAsync(
        Guid produtoId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemOperacaoResult> ConsultarAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemConteudoResult> AbrirConteudoAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemConteudoResult> AbrirConteudoPublicoAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemOperacaoResult> DefinirPrincipalAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemOperacaoResult> ReordenarAsync(
        Guid produtoId,
        IReadOnlyList<Guid>? imagemIds,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemOperacaoResult> AssociarVarianteAsync(
        Guid produtoId,
        Guid imagemId,
        Guid varianteId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemOperacaoResult> DesassociarVarianteAsync(
        Guid produtoId,
        Guid imagemId,
        Guid varianteId,
        CancellationToken cancellationToken = default);

    Task<ProdutoImagemOperacaoResult> RemoverAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default);
}
