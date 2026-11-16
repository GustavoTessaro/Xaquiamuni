namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public enum ProdutoImagemOperacaoStatus
{
    Success,
    ValidationFailed,
    ProductNotFound,
    ImageNotFound,
    VariantNotFound,
    Conflict,
    InvalidImage,
    StorageUnavailable,
    StoredFileMissing,
    CommitOutcomeUnknown,
    CompensationPending,
    CleanupPending
}
