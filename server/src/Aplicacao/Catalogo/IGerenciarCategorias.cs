using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public interface IGerenciarCategorias
{
    Task<CategoriaResult> CadastrarAsync(
        CategoriaInput? input,
        CancellationToken cancellationToken = default);

    Task<Categoria?> ConsultarAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Categoria>> ListarAsync(
        CancellationToken cancellationToken = default);

    Task<CategoriaResult> EditarAsync(
        Guid id,
        CategoriaInput? input,
        CancellationToken cancellationToken = default);

    Task<CategoriaResult> AlterarStatusAsync(
        Guid id,
        bool? ativa,
        CancellationToken cancellationToken = default);
}
