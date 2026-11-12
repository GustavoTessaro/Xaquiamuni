namespace Xaquiamuni.Aplicacao.Outbox;

public sealed class EmailOutboxClaim
{
    private readonly byte[] _rowVersion;
    private readonly byte[]? _credentialGenerationFingerprint;

    public EmailOutboxClaim(
        Guid id,
        Guid userId,
        EmailOutboxMessageType messageType,
        int attempts,
        DateTimeOffset leaseExpiresAtUtc,
        byte[] rowVersion,
        byte[]? credentialGenerationFingerprint = null)
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

        if (messageType == EmailOutboxMessageType.PasswordReset)
        {
            ArgumentNullException.ThrowIfNull(credentialGenerationFingerprint);
            if (credentialGenerationFingerprint.Length != 32)
            {
                throw new ArgumentException(
                    "The credential generation fingerprint must be 32 bytes.",
                    nameof(credentialGenerationFingerprint));
            }
        }
        else if (credentialGenerationFingerprint is not null)
        {
            throw new ArgumentException(
                "Email confirmation claims cannot contain a credential generation fingerprint.",
                nameof(credentialGenerationFingerprint));
        }

        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        if (leaseExpiresAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The lease timestamp must use UTC.", nameof(leaseExpiresAtUtc));
        }

        ArgumentNullException.ThrowIfNull(rowVersion);
        if (rowVersion.Length != 8)
        {
            throw new ArgumentException("The SQL Server row version must be eight bytes.", nameof(rowVersion));
        }

        Id = id;
        UserId = userId;
        MessageType = messageType;
        Attempts = attempts;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        _rowVersion = (byte[])rowVersion.Clone();
        _credentialGenerationFingerprint = credentialGenerationFingerprint is null
            ? null
            : (byte[])credentialGenerationFingerprint.Clone();
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public EmailOutboxMessageType MessageType { get; }
    public int Attempts { get; }
    public DateTimeOffset LeaseExpiresAtUtc { get; }
    public ReadOnlyMemory<byte> RowVersion => _rowVersion;
    public ReadOnlyMemory<byte>? CredentialGenerationFingerprint
    {
        get
        {
            if (_credentialGenerationFingerprint is null)
            {
                return null;
            }

            return new ReadOnlyMemory<byte>(_credentialGenerationFingerprint);
        }
    }

    public override string ToString() => "[EmailOutboxClaim]";
}
