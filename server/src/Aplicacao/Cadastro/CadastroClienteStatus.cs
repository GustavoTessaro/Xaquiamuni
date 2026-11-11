namespace Xaquiamuni.Aplicacao.Cadastro;

public enum CadastroClienteStatus
{
    Success,
    ValidationFailed,
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
        IReadOnlyList<CadastroClienteInputValidationError> validationErrors)
    {
        Status = status;
        UserId = userId;
        ValidationErrors = validationErrors;
    }

    public CadastroClienteStatus Status { get; }
    public Guid? UserId { get; }
    public IReadOnlyList<CadastroClienteInputValidationError> ValidationErrors { get; }
    public bool Succeeded => Status == CadastroClienteStatus.Success;

    internal static CadastroClienteResult Success(Guid userId) =>
        new(CadastroClienteStatus.Success, userId, Array.Empty<CadastroClienteInputValidationError>());

    internal static CadastroClienteResult Failure(CadastroClienteStatus status) =>
        new(status, null, Array.Empty<CadastroClienteInputValidationError>());

    internal static CadastroClienteResult Invalid(
        IReadOnlyList<CadastroClienteInputValidationError> errors) =>
        new(CadastroClienteStatus.ValidationFailed, null, errors);

    public override string ToString() => $"[CadastroCliente {Status}]";
}
