using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed class ProdutoResult
{
    private ProdutoResult(
        ProdutoStatus status,
        Produto? produto,
        IReadOnlyList<ProdutoValidationError> erros,
        string? mensagem)
    {
        Status = status;
        Produto = produto;
        Erros = erros;
        Mensagem = mensagem;
    }

    public ProdutoStatus Status { get; }
    public Produto? Produto { get; }
    public IReadOnlyList<ProdutoValidationError> Erros { get; }
    public string? Mensagem { get; }

    public static ProdutoResult Success(Produto produto) =>
        new(ProdutoStatus.Success, produto, Array.Empty<ProdutoValidationError>(), null);

    public static ProdutoResult ValidationFailed(ProdutoValidationError erro) =>
        new(ProdutoStatus.ValidationFailed, null, [erro], null);

    public static ProdutoResult ProductNotFound() =>
        new(ProdutoStatus.ProductNotFound, null, Array.Empty<ProdutoValidationError>(), null);

    public static ProdutoResult CategoryNotFound() =>
        new(ProdutoStatus.CategoryNotFound, null, Array.Empty<ProdutoValidationError>(), null);

    public static ProdutoResult VariantNotFound() =>
        new(ProdutoStatus.VariantNotFound, null, Array.Empty<ProdutoValidationError>(), null);

    public static ProdutoResult CategoryInactive() =>
        new(
            ProdutoStatus.CategoryInactive,
            null,
            Array.Empty<ProdutoValidationError>(),
            "A categoria está inativa e não pode ser atribuída ou publicada.");

    public static ProdutoResult Conflict(string mensagem) =>
        new(ProdutoStatus.Conflict, null, Array.Empty<ProdutoValidationError>(), mensagem);
}
