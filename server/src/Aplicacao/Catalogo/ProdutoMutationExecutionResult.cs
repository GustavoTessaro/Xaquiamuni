namespace Xaquiamuni.Aplicacao.Catalogo;

public enum ProdutoMutationPersistenceState
{
    Committed,
    RolledBack,
    Unknown
}

public sealed class ProdutoMutationExecutionResult
{
    private ProdutoMutationExecutionResult(
        ProdutoMutationPersistenceState persistenceState,
        ProdutoResult? result,
        Exception? failure)
    {
        PersistenceState = persistenceState;
        Result = result;
        Failure = failure;
    }

    public ProdutoMutationPersistenceState PersistenceState { get; }
    public ProdutoResult? Result { get; }
    public Exception? Failure { get; }

    public static ProdutoMutationExecutionResult Committed(ProdutoResult result) =>
        new(ProdutoMutationPersistenceState.Committed, result, null);

    public static ProdutoMutationExecutionResult RolledBack(ProdutoResult result) =>
        new(ProdutoMutationPersistenceState.RolledBack, result, null);

    public static ProdutoMutationExecutionResult Failed(
        ProdutoMutationPersistenceState persistenceState,
        Exception failure) =>
        new(persistenceState, null, failure);
}
