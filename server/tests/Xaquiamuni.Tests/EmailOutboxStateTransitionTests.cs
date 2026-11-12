using Xaquiamuni.Infraestrutura.Persistencia.Outbox;
using Xaquiamuni.Aplicacao.Outbox;

namespace Xaquiamuni.Tests;

public sealed class EmailOutboxStateTransitionTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryClaim_PendingMessage_ClaimsAndIncrementsAttempts()
    {
        var message = CreateMessage();

        var claimed = message.TryClaim(UtcNow, TimeSpan.FromMinutes(2));

        Assert.True(claimed);
        Assert.Equal(EmailOutboxMessageStatus.Processing, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Equal(UtcNow.AddMinutes(2), message.LeaseExpiresAtUtc);
        Assert.Null(message.CompletedAtUtc);
        Assert.Equal("email-confirmation:test", message.ActiveDeduplicationKey);
    }

    [Fact]
    public void TryClaim_RejectsNotDuePendingAndUnexpiredLeases()
    {
        var pending = CreateMessage(nextAttemptAtUtc: UtcNow.AddSeconds(1));
        var processing = CreateClaimedMessage(UtcNow.AddMinutes(2));

        Assert.False(pending.TryClaim(UtcNow, TimeSpan.FromMinutes(2)));
        Assert.False(processing.TryClaim(UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(EmailOutboxMessageStatus.Pending, pending.Status);
        Assert.Equal(EmailOutboxMessageStatus.Processing, processing.Status);
        Assert.Equal(1, processing.Attempts);
    }

    [Fact]
    public void TryClaim_ReclaimsExpiredLeaseAndDeadLettersAtAttemptLimit()
    {
        var expired = CreateClaimedMessage(UtcNow);
        Assert.True(expired.TryClaim(UtcNow, TimeSpan.FromMinutes(1)));
        Assert.Equal(2, expired.Attempts);

        var exhausted = CreateClaimedMessage(UtcNow, attempts: EmailOutboxRetryPolicy.MaximumAttempts);
        Assert.False(exhausted.TryClaim(UtcNow, TimeSpan.FromMinutes(1)));
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, exhausted.Status);
        Assert.Null(exhausted.LeaseExpiresAtUtc);
        Assert.Equal("email-confirmation:test", exhausted.ActiveDeduplicationKey);
    }

    [Fact]
    public void Complete_RequiresUnexpiredLeaseAndClearsActiveDeduplication()
    {
        var message = CreateClaimedMessage(UtcNow.AddMinutes(1));

        message.Complete(UtcNow);

        Assert.Equal(EmailOutboxMessageStatus.Completed, message.Status);
        Assert.Equal(UtcNow, message.CompletedAtUtc);
        Assert.Null(message.LeaseExpiresAtUtc);
        Assert.Null(message.ActiveDeduplicationKey);
        Assert.Throws<InvalidOperationException>(() => message.Complete(UtcNow));
    }

    [Fact]
    public void RetryTransientFailure_UsesBoundedDeterministicJitterAndPreservesDeduplication()
    {
        var message = CreateClaimedMessage(UtcNow.AddMinutes(1));

        message.RetryTransientFailure(UtcNow, jitterSample: 0);

        Assert.Equal(EmailOutboxMessageStatus.Pending, message.Status);
        Assert.Equal(UtcNow.AddSeconds(96), message.NextAttemptAtUtc);
        Assert.Null(message.LeaseExpiresAtUtc);
        Assert.Null(message.CompletedAtUtc);
        Assert.Equal("email-confirmation:test", message.ActiveDeduplicationKey);
    }

    [Fact]
    public void RetryPolicy_UsesExponentialDelayWithConfiguredCap()
    {
        Assert.Equal(
            EmailOutboxRetryPolicy.MaximumDelay,
            EmailOutboxRetryPolicy.GetRetryDelay(attempt: 4, jitterSample: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmailOutboxRetryPolicy.GetRetryDelay(attempt: 0, jitterSample: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmailOutboxRetryPolicy.GetRetryDelay(
                attempt: EmailOutboxRetryPolicy.MaximumAttempts,
                jitterSample: 0.5));
    }

    [Fact]
    public void RetryTransientFailure_DeadLettersAfterFifthAttempt()
    {
        var message = CreateClaimedMessage(
            UtcNow.AddMinutes(1),
            attempts: EmailOutboxRetryPolicy.MaximumAttempts);

        message.RetryTransientFailure(UtcNow, jitterSample: 1);

        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, message.Status);
        Assert.Null(message.LeaseExpiresAtUtc);
        Assert.Null(message.CompletedAtUtc);
        Assert.Equal("email-confirmation:test", message.ActiveDeduplicationKey);
    }

    [Fact]
    public void PermanentFailure_DeadLettersAndCancellationReleasesDeduplication()
    {
        var deadLetter = CreateClaimedMessage(UtcNow.AddMinutes(1));
        deadLetter.MarkPermanentFailure(UtcNow);
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, deadLetter.Status);
        Assert.Equal("email-confirmation:test", deadLetter.ActiveDeduplicationKey);

        var pending = CreateMessage();
        pending.Cancel();
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, pending.Status);
        Assert.Null(pending.ActiveDeduplicationKey);
        Assert.Null(pending.LeaseExpiresAtUtc);

        var processing = CreateClaimedMessage(UtcNow.AddMinutes(1));
        processing.Cancel();
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, processing.Status);
        Assert.Null(processing.ActiveDeduplicationKey);
        Assert.Null(processing.LeaseExpiresAtUtc);
        Assert.Throws<InvalidOperationException>(processing.Cancel);
    }

    [Fact]
    public void Transitions_RejectExpiredLeasesNonUtcTimesAndInvalidJitter()
    {
        var expired = CreateClaimedMessage(UtcNow);
        Assert.Throws<InvalidOperationException>(() => expired.Complete(UtcNow));
        Assert.Throws<InvalidOperationException>(
            () => expired.RetryTransientFailure(UtcNow, jitterSample: 0.5));

        var pending = CreateMessage();
        Assert.Throws<ArgumentException>(
            () => pending.TryClaim(UtcNow.ToOffset(TimeSpan.FromHours(-3)), TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => pending.TryClaim(UtcNow, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmailOutboxRetryPolicy.GetRetryDelay(attempt: 1, jitterSample: double.NaN));
    }

    private static EmailOutboxMessage CreateMessage(DateTimeOffset? nextAttemptAtUtc = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailOutboxMessageType.EmailConfirmation,
            EmailOutboxMessageStatus.Pending,
            attempts: 0,
            createdAtUtc: UtcNow,
            nextAttemptAtUtc: nextAttemptAtUtc ?? UtcNow,
            activeDeduplicationKey: "email-confirmation:test");

    private static EmailOutboxMessage CreateClaimedMessage(
        DateTimeOffset leaseExpiresAtUtc,
        int attempts = 1) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailOutboxMessageType.EmailConfirmation,
            EmailOutboxMessageStatus.Processing,
            attempts,
            createdAtUtc: UtcNow,
            nextAttemptAtUtc: UtcNow,
            activeDeduplicationKey: "email-confirmation:test",
            leaseExpiresAtUtc: leaseExpiresAtUtc);
}
