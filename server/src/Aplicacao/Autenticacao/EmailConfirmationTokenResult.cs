namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum EmailConfirmationTokenStatus
{
    Generated,
    UserNotFound,
    AlreadyConfirmed
}

public sealed class EmailConfirmationTokenResult
{
    private EmailConfirmationTokenResult(EmailConfirmationTokenStatus status, string? token)
    {
        Status = status;
        Token = token;
    }

    public EmailConfirmationTokenStatus Status { get; }
    public string? Token { get; }

    public static EmailConfirmationTokenResult Generated(string token) =>
        new(EmailConfirmationTokenStatus.Generated, token);

    public static EmailConfirmationTokenResult UserNotFound() =>
        new(EmailConfirmationTokenStatus.UserNotFound, null);

    public static EmailConfirmationTokenResult AlreadyConfirmed() =>
        new(EmailConfirmationTokenStatus.AlreadyConfirmed, null);

    public override string ToString() => $"[EmailConfirmationToken {Status}]";
}
