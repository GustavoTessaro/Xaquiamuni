namespace Xaquiamuni.Aplicacao.Cadastro;

public enum CadastroClienteStatus
{
    Success,
    ValidationFailed,
    PasswordValidationFailed,
    RoleUnavailable,
    EmailAlreadyRegistered,
    CpfAlreadyRegistered,
    IdentityCreationFailed,
    RoleAssignmentFailed,
    PersistenceFailed
}

public sealed class CadastroClienteResult
{
    private CadastroClienteResult(
        CadastroClienteStatus status,
        Guid? userId,
        IReadOnlyList<CadastroClienteInputValidationError> validationErrors,
        CadastroClienteFailureCategory? failureCategory = null,
        CadastroClienteFailureStage? failureStage = null,
        IReadOnlyList<string>? safeDiagnosticCodes = null)
    {
        Status = status;
        UserId = userId;
        ValidationErrors = validationErrors;
        FailureCategory = failureCategory;
        FailureStage = failureStage;
        SafeDiagnosticCodes = safeDiagnosticCodes ?? Array.Empty<string>();
    }

    public CadastroClienteStatus Status { get; }
    public Guid? UserId { get; }
    public IReadOnlyList<CadastroClienteInputValidationError> ValidationErrors { get; }
    public CadastroClienteFailureCategory? FailureCategory { get; }
    public CadastroClienteFailureStage? FailureStage { get; }
    public IReadOnlyList<string> SafeDiagnosticCodes { get; }
    public bool Succeeded => Status == CadastroClienteStatus.Success;

    internal static CadastroClienteResult Success(Guid userId) =>
        new(CadastroClienteStatus.Success, userId, Array.Empty<CadastroClienteInputValidationError>());

    internal static CadastroClienteResult Failure(
        CadastroClienteStatus status,
        CadastroClienteFailureCategory category,
        CadastroClienteFailureStage stage,
        IReadOnlyList<string>? safeDiagnosticCodes = null) =>
        new(
            status,
            null,
            Array.Empty<CadastroClienteInputValidationError>(),
            category,
            stage,
            safeDiagnosticCodes);

    internal static CadastroClienteResult Invalid(
        IReadOnlyList<CadastroClienteInputValidationError> errors) =>
        new(CadastroClienteStatus.ValidationFailed, null, errors);

    public override string ToString() => $"[CadastroCliente {Status}]";
}
