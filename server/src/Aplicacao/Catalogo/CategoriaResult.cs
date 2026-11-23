using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed class CategoriaResult
{
    private CategoriaResult(
        CategoriaStatus status,
        Categoria? categoria,
        IReadOnlyList<CategoriaValidationError> erros)
    {
        Status = status;
        Categoria = categoria;
        Erros = erros;
    }

    public CategoriaStatus Status { get; }
    public Categoria? Categoria { get; }
    public IReadOnlyList<CategoriaValidationError> Erros { get; }

    public static CategoriaResult Success(Categoria categoria) =>
        new(CategoriaStatus.Success, categoria, Array.Empty<CategoriaValidationError>());

    public static CategoriaResult ValidationFailed(CategoriaValidationError erro) =>
        new(CategoriaStatus.ValidationFailed, null, [erro]);

    public static CategoriaResult NotFound() =>
        new(CategoriaStatus.NotFound, null, Array.Empty<CategoriaValidationError>());

    public static CategoriaResult VersionConflict() => new(CategoriaStatus.VersionConflict, null, []);

    public static CategoriaResult NameAlreadyExists() =>
        new(CategoriaStatus.NameAlreadyExists, null, Array.Empty<CategoriaValidationError>());
}
