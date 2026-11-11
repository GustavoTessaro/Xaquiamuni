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

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        return value;
    }
}
