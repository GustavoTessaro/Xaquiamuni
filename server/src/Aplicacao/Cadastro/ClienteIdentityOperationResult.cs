namespace Xaquiamuni.Aplicacao.Cadastro;

public enum ClienteIdentityOperationStatus
{
    Success,
    DuplicateEmail,
    PersistenceFailed,
    Failed
}

public sealed class ClienteIdentityOperationResult
{
    private ClienteIdentityOperationResult(ClienteIdentityOperationStatus status)
    {
        Status = status;
    }

    public ClienteIdentityOperationStatus Status { get; }
    public bool Succeeded => Status == ClienteIdentityOperationStatus.Success;

    public static ClienteIdentityOperationResult Success() =>
        new(ClienteIdentityOperationStatus.Success);

    public static ClienteIdentityOperationResult DuplicateEmail() =>
        new(ClienteIdentityOperationStatus.DuplicateEmail);

    public static ClienteIdentityOperationResult PersistenceFailed() =>
        new(ClienteIdentityOperationStatus.PersistenceFailed);

    public static ClienteIdentityOperationResult Failed() =>
        new(ClienteIdentityOperationStatus.Failed);

    public override string ToString() => $"[Identity {Status}]";
}
