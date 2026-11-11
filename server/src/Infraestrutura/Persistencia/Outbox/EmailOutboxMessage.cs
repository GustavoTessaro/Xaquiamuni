namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

public enum EmailOutboxMessageType : short
{
    EmailConfirmation = 1
}

public enum EmailOutboxMessageStatus : short
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    DeadLetter = 4,
    Cancelled = 5
}

public sealed class EmailOutboxMessage
{
    public const int ActiveDeduplicationKeyMaxLength = 128;

    private EmailOutboxMessage()
    {
    }

    public EmailOutboxMessage(
        Guid id,
        Guid userId,
        EmailOutboxMessageType messageType,
        EmailOutboxMessageStatus status,
        int attempts,
        DateTimeOffset createdAtUtc,
        DateTimeOffset nextAttemptAtUtc,
        DateTimeOffset? completedAtUtc = null,
        string? activeDeduplicationKey = null,
        DateTimeOffset? leaseExpiresAtUtc = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The outbox message identifier is required.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        if (!Enum.IsDefined(messageType))
        {
            throw new ArgumentOutOfRangeException(nameof(messageType));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (attempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        if (activeDeduplicationKey is not null
            && (string.IsNullOrWhiteSpace(activeDeduplicationKey)
                || activeDeduplicationKey.Length > ActiveDeduplicationKeyMaxLength))
        {
            throw new ArgumentException("The active deduplication key is invalid.", nameof(activeDeduplicationKey));
        }

        Id = id;
        UserId = userId;
        MessageType = messageType;
        Status = status;
        Attempts = attempts;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        NextAttemptAtUtc = RequireUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        CompletedAtUtc = completedAtUtc is null
            ? null
            : RequireUtc(completedAtUtc.Value, nameof(completedAtUtc));
        ActiveDeduplicationKey = activeDeduplicationKey;
        LeaseExpiresAtUtc = leaseExpiresAtUtc is null
            ? null
            : RequireUtc(leaseExpiresAtUtc.Value, nameof(leaseExpiresAtUtc));

        ValidateInitialState();
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public EmailOutboxMessageType MessageType { get; private set; }
    public EmailOutboxMessageStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? ActiveDeduplicationKey { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public override string ToString() => "[EmailOutboxMessage]";

    public bool TryClaim(DateTimeOffset nowUtc, TimeSpan leaseDuration)
    {
        nowUtc = RequireUtc(nowUtc, nameof(nowUtc));
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var claimable = Status switch
        {
            EmailOutboxMessageStatus.Pending => NextAttemptAtUtc <= nowUtc,
            EmailOutboxMessageStatus.Processing =>
                LeaseExpiresAtUtc is not null && LeaseExpiresAtUtc.Value <= nowUtc,
            _ => false
        };

        if (!claimable)
        {
            return false;
        }

        if (Attempts >= EmailOutboxRetryPolicy.MaximumAttempts)
        {
            MoveToDeadLetter();
            return false;
        }

        Attempts++;
        Status = EmailOutboxMessageStatus.Processing;
        LeaseExpiresAtUtc = nowUtc + leaseDuration;
        return true;
    }

    public void Complete(DateTimeOffset completedAtUtc)
    {
        EnsureActiveLease(completedAtUtc);

        Status = EmailOutboxMessageStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        LeaseExpiresAtUtc = null;
        ActiveDeduplicationKey = null;
    }

    public void RetryTransientFailure(DateTimeOffset failedAtUtc, double jitterSample)
    {
        EnsureActiveLease(failedAtUtc);

        if (Attempts >= EmailOutboxRetryPolicy.MaximumAttempts)
        {
            MoveToDeadLetter();
            return;
        }

        var retryDelay = EmailOutboxRetryPolicy.GetRetryDelay(Attempts, jitterSample);
        NextAttemptAtUtc = failedAtUtc + retryDelay;
        Status = EmailOutboxMessageStatus.Pending;
        LeaseExpiresAtUtc = null;
    }

    public void MarkPermanentFailure(DateTimeOffset failedAtUtc)
    {
        EnsureActiveLease(failedAtUtc);
        MoveToDeadLetter();
    }

    public void Cancel()
    {
        if (Status is not (EmailOutboxMessageStatus.Pending or EmailOutboxMessageStatus.Processing))
        {
            throw new InvalidOperationException("Only pending or processing messages can be cancelled.");
        }

        Status = EmailOutboxMessageStatus.Cancelled;
        CompletedAtUtc = null;
        LeaseExpiresAtUtc = null;
        ActiveDeduplicationKey = null;
    }

    private void EnsureActiveLease(DateTimeOffset nowUtc)
    {
        nowUtc = RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != EmailOutboxMessageStatus.Processing
            || LeaseExpiresAtUtc is null
            || LeaseExpiresAtUtc.Value <= nowUtc)
        {
            throw new InvalidOperationException("The message does not have an active processing lease.");
        }
    }

    private void MoveToDeadLetter()
    {
        Status = EmailOutboxMessageStatus.DeadLetter;
        CompletedAtUtc = null;
        LeaseExpiresAtUtc = null;
    }

    private void ValidateInitialState()
    {
        var valid = Status switch
        {
            EmailOutboxMessageStatus.Pending =>
                CompletedAtUtc is null && LeaseExpiresAtUtc is null,
            EmailOutboxMessageStatus.Processing =>
                CompletedAtUtc is null && LeaseExpiresAtUtc is not null && Attempts > 0,
            EmailOutboxMessageStatus.Completed =>
                CompletedAtUtc is not null
                && LeaseExpiresAtUtc is null
                && ActiveDeduplicationKey is null,
            EmailOutboxMessageStatus.DeadLetter =>
                CompletedAtUtc is null && LeaseExpiresAtUtc is null,
            EmailOutboxMessageStatus.Cancelled =>
                CompletedAtUtc is null
                && LeaseExpiresAtUtc is null
                && ActiveDeduplicationKey is null,
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException("The initial message state is inconsistent.", nameof(Status));
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        return value;
    }
}
