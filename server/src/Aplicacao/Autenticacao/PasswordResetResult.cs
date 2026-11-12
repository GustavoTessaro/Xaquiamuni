namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum PasswordResetStatus
{
    Succeeded,
    InvalidPassword,
    InvalidRequest
}

public sealed class PasswordResetResult
{
    private PasswordResetResult(PasswordResetStatus status)
    {
        Status = status;
    }

    public PasswordResetStatus Status { get; }
    public bool Succeeded => Status == PasswordResetStatus.Succeeded;

    public static PasswordResetResult Success() =>
        new(PasswordResetStatus.Succeeded);

    public static PasswordResetResult InvalidPassword() =>
        new(PasswordResetStatus.InvalidPassword);

    public static PasswordResetResult InvalidRequest() =>
        new(PasswordResetStatus.InvalidRequest);

    public override string ToString() => $"[PasswordReset {Status}]";
}
