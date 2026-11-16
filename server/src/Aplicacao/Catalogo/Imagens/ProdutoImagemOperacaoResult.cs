using Xaquiamuni.Aplicacao.Catalogo;

namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public sealed class ProdutoImagemOperacaoResult
{
    private ProdutoImagemOperacaoResult(
        ProdutoImagemOperacaoStatus status,
        ProdutoImagemDto? imagem,
        IReadOnlyList<ProdutoImagemDto> imagens,
        IReadOnlyList<ProdutoValidationError> erros,
        string? mensagem)
    {
        Status = status;
        Imagem = imagem;
        Imagens = imagens;
        Erros = erros;
        Mensagem = mensagem;
    }

    public ProdutoImagemOperacaoStatus Status { get; }
    public ProdutoImagemDto? Imagem { get; }
    public IReadOnlyList<ProdutoImagemDto> Imagens { get; }
    public IReadOnlyList<ProdutoValidationError> Erros { get; }
    public string? Mensagem { get; }

    public static ProdutoImagemOperacaoResult Success(
        ProdutoImagemDto? imagem = null,
        IReadOnlyList<ProdutoImagemDto>? imagens = null) =>
        new(
            ProdutoImagemOperacaoStatus.Success,
            imagem,
            imagens ?? Array.Empty<ProdutoImagemDto>(),
            Array.Empty<ProdutoValidationError>(),
            null);

    public static ProdutoImagemOperacaoResult ValidationFailed(
        string campo,
        string mensagem) =>
        new(
            ProdutoImagemOperacaoStatus.ValidationFailed,
            null,
            Array.Empty<ProdutoImagemDto>(),
            [new ProdutoValidationError(campo, mensagem)],
            null);

    public static ProdutoImagemOperacaoResult ProductNotFound() =>
        Empty(ProdutoImagemOperacaoStatus.ProductNotFound);

    public static ProdutoImagemOperacaoResult ImageNotFound() =>
        Empty(ProdutoImagemOperacaoStatus.ImageNotFound);

    public static ProdutoImagemOperacaoResult VariantNotFound() =>
        Empty(ProdutoImagemOperacaoStatus.VariantNotFound);

    public static ProdutoImagemOperacaoResult Conflict(string? mensagem = null) =>
        Empty(ProdutoImagemOperacaoStatus.Conflict, mensagem);

    public static ProdutoImagemOperacaoResult InvalidImage(string mensagem) =>
        Empty(ProdutoImagemOperacaoStatus.InvalidImage, mensagem);

    public static ProdutoImagemOperacaoResult StorageUnavailable() =>
        Empty(ProdutoImagemOperacaoStatus.StorageUnavailable);

    public static ProdutoImagemOperacaoResult CommitOutcomeUnknown() =>
        Empty(
            ProdutoImagemOperacaoStatus.CommitOutcomeUnknown,
            "O resultado da persistência é incerto; a imagem será mantida para reconciliação.");

    public static ProdutoImagemOperacaoResult CompensationPending() =>
        Empty(
            ProdutoImagemOperacaoStatus.CompensationPending,
            "A persistência foi revertida, mas a limpeza do arquivo está pendente.");

    public static ProdutoImagemOperacaoResult CleanupPending() =>
        Empty(
            ProdutoImagemOperacaoStatus.CleanupPending,
            "Os metadados foram removidos, mas a exclusão física da imagem está pendente.");

    private static ProdutoImagemOperacaoResult Empty(
        ProdutoImagemOperacaoStatus status,
        string? mensagem = null) =>
        new(
            status,
            null,
            Array.Empty<ProdutoImagemDto>(),
            Array.Empty<ProdutoValidationError>(),
            mensagem);
}
