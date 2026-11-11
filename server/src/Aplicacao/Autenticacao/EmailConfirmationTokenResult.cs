namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum EmailConfirmationTokenStatus
{
    Generated,
    UserNotFound,
    EmailMissing,
    AlreadyConfirmed
}

public sealed class EmailConfirmationTokenResult
{
    private EmailConfirmationTokenResult(
        EmailConfirmationTokenStatus status,
        string? email,
        string? token)
    {
        Status = status;
        Email = email;
        Token = token;
    }

    public EmailConfirmationTokenStatus Status { get; }
    public string? Email { get; }
    public string? Token { get; }

    public static EmailConfirmationTokenResult Generated(string token) =>
        new(EmailConfirmationTokenStatus.Generated, null, token);

    public static EmailConfirmationTokenResult GeneratedForUser(string email, string token) =>
        new(EmailConfirmationTokenStatus.Generated, email, token);

    public static EmailConfirmationTokenResult UserNotFound() =>
        new(EmailConfirmationTokenStatus.UserNotFound, null, null);

    public static EmailConfirmationTokenResult EmailMissing() =>
        new(EmailConfirmationTokenStatus.EmailMissing, null, null);

    public static EmailConfirmationTokenResult AlreadyConfirmed() =>
        new(EmailConfirmationTokenStatus.AlreadyConfirmed, null, null);

    public override string ToString() => $"[EmailConfirmationToken {Status}]";
}
