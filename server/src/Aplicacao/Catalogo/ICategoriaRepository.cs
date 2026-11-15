using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public interface ICategoriaRepository
{
    Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken cancellationToken = default);

    Task<Categoria?> BuscarPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> ExisteNomeNormalizadoAsync(
        string nomeNormalizado,
        Guid? excetoId = null,
        CancellationToken cancellationToken = default);

    Task<bool> AdicionarAsync(
        Categoria categoria,
        CancellationToken cancellationToken = default);

    Task<bool> AtualizarAsync(
        Categoria categoria,
        CancellationToken cancellationToken = default);
}
