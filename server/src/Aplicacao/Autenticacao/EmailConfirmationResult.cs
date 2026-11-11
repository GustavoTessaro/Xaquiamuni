namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum EmailConfirmationStatus
{
    Confirmed,
    UserNotFound,
    InvalidOrExpiredToken,
    AlreadyConfirmed
}

public sealed class EmailConfirmationResult
{
    private EmailConfirmationResult(EmailConfirmationStatus status)
    {
        Status = status;
    }

    public EmailConfirmationStatus Status { get; }
    public bool Succeeded => Status == EmailConfirmationStatus.Confirmed;

    public static EmailConfirmationResult Confirmed() =>
        new(EmailConfirmationStatus.Confirmed);

    public static EmailConfirmationResult UserNotFound() =>
        new(EmailConfirmationStatus.UserNotFound);

    public static EmailConfirmationResult InvalidOrExpiredToken() =>
        new(EmailConfirmationStatus.InvalidOrExpiredToken);

    public static EmailConfirmationResult AlreadyConfirmed() =>
        new(EmailConfirmationStatus.AlreadyConfirmed);

    public override string ToString() => $"[EmailConfirmation {Status}]";
}
