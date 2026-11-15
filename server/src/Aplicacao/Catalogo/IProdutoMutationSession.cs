using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public interface IProdutoMutationSession
{
    Task<Categoria?> BuscarCategoriaAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Produto?> BuscarProdutoAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    void Adicionar(Produto produto);
}
