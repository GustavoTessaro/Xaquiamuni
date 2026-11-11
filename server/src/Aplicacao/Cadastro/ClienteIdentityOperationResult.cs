namespace Xaquiamuni.Aplicacao.Cadastro;

public enum ClienteIdentityOperationStatus
{
    Success,
    PasswordValidationFailed,
    DuplicateEmail,
    PersistenceFailed,
    Failed
}

public sealed class ClienteIdentityOperationResult
{
    private ClienteIdentityOperationResult(
        ClienteIdentityOperationStatus status,
        IReadOnlyList<string> safeDiagnosticCodes)
    {
        Status = status;
        SafeDiagnosticCodes = safeDiagnosticCodes;
    }

    public ClienteIdentityOperationStatus Status { get; }
    public IReadOnlyList<string> SafeDiagnosticCodes { get; }
    public bool Succeeded => Status == ClienteIdentityOperationStatus.Success;

    public static ClienteIdentityOperationResult Success() =>
        new(ClienteIdentityOperationStatus.Success, Array.Empty<string>());

    public static ClienteIdentityOperationResult DuplicateEmail() =>
        new(ClienteIdentityOperationStatus.DuplicateEmail, Array.Empty<string>());

    public static ClienteIdentityOperationResult PasswordValidationFailed(
        IEnumerable<string> safeDiagnosticCodes) =>
        new(
            ClienteIdentityOperationStatus.PasswordValidationFailed,
            CadastroClienteDiagnostics.SanitizeIdentityErrorCodes(safeDiagnosticCodes));

    public static ClienteIdentityOperationResult PersistenceFailed() =>
        new(ClienteIdentityOperationStatus.PersistenceFailed, Array.Empty<string>());

    public static ClienteIdentityOperationResult Failed(
        IEnumerable<string>? safeDiagnosticCodes = null) =>
        new(
            ClienteIdentityOperationStatus.Failed,
            safeDiagnosticCodes is null
                ? Array.Empty<string>()
                : CadastroClienteDiagnostics.SanitizeIdentityErrorCodes(safeDiagnosticCodes));

    public override string ToString() => $"[Identity {Status}]";
}
