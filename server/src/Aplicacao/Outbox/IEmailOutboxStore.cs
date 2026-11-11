namespace Xaquiamuni.Aplicacao.Outbox;

public interface IEmailOutboxStore
{
    Task<IReadOnlyList<EmailOutboxClaim>> ClaimDueAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default);

    Task<bool> RetryAsync(
        EmailOutboxClaim claim,
        double jitterSample,
        CancellationToken cancellationToken = default);

    Task<bool> DeadLetterAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default);
}
