namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum PasswordResetTokenStatus
{
    Generated,
    NotEligible,
    CredentialGenerationMismatch,
    InvalidRequest
}

public sealed class PasswordResetTokenResult
{
    private PasswordResetTokenResult(
        PasswordResetTokenStatus status,
        string? email,
        string? token)
    {
        Status = status;
        Email = email;
        Token = token;
    }

    public PasswordResetTokenStatus Status { get; }
    public string? Email { get; }
    public string? Token { get; }

    public static PasswordResetTokenResult Generated(string email, string token) =>
        new(PasswordResetTokenStatus.Generated, email, token);

    public static PasswordResetTokenResult NotEligible() =>
        new(PasswordResetTokenStatus.NotEligible, null, null);

    public static PasswordResetTokenResult CredentialGenerationMismatch() =>
        new(PasswordResetTokenStatus.CredentialGenerationMismatch, null, null);

    public static PasswordResetTokenResult InvalidRequest() =>
        new(PasswordResetTokenStatus.InvalidRequest, null, null);

    public override string ToString() => $"[PasswordResetToken {Status}]";
}
