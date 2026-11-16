using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public interface IProdutoRepository
{
    Task<IReadOnlyList<Produto>> ListarAsync(
        ProdutoFiltro filtro,
        CancellationToken cancellationToken = default);

    Task<Produto?> ConsultarAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ProdutoResult> ExecutarMutacaoAsync(
        Func<IProdutoMutationSession, CancellationToken, Task<ProdutoResult>> operacao,
        CancellationToken cancellationToken = default);

    Task<ProdutoMutationExecutionResult> ExecutarMutacaoComResultadoAsync(
        Func<IProdutoMutationSession, CancellationToken, Task<ProdutoResult>> operacao,
        CancellationToken cancellationToken = default);
}
