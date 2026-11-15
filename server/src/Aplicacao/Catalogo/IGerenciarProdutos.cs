using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public interface IGerenciarProdutos
{
    Task<IReadOnlyList<Produto>> ListarAsync(
        ProdutoFiltro filtro,
        CancellationToken cancellationToken = default);

    Task<Produto?> ConsultarAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> CadastrarAsync(
        ProdutoCadastroInput? input,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> EditarAsync(
        Guid id,
        ProdutoEdicaoInput? input,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> AdicionarVarianteAsync(
        Guid produtoId,
        ProdutoVarianteInput? input,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> EditarVarianteAsync(
        Guid produtoId,
        Guid varianteId,
        ProdutoVarianteInput? input,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> DefinirVariantePadraoAsync(
        Guid produtoId,
        Guid varianteId,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> AlterarStatusVarianteAsync(
        Guid produtoId,
        Guid varianteId,
        bool? ativa,
        Guid? novoPadraoId,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> AlterarPublicacaoAsync(
        Guid produtoId,
        bool? publicado,
        CancellationToken cancellationToken = default);
}
