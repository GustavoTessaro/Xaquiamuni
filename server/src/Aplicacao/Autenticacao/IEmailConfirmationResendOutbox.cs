namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum EmailConfirmationResendOutboxResult
{
    Enqueued,
    NotEligible,
    AlreadyQueued,
    CooldownActive,
    HourlyLimitReached
}

public interface IEmailConfirmationResendOutbox
{
    Task<EmailConfirmationResendOutboxResult> RequestAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
