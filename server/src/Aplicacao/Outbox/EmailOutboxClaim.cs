namespace Xaquiamuni.Aplicacao.Outbox;

public sealed class EmailOutboxClaim
{
    private readonly byte[] _rowVersion;

    public EmailOutboxClaim(
        Guid id,
        Guid userId,
        int attempts,
        DateTimeOffset leaseExpiresAtUtc,
        byte[] rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The outbox message identifier is required.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
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
        Attempts = attempts;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        _rowVersion = (byte[])rowVersion.Clone();
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public int Attempts { get; }
    public DateTimeOffset LeaseExpiresAtUtc { get; }
    public ReadOnlyMemory<byte> RowVersion => _rowVersion;

    public override string ToString() => "[EmailOutboxClaim]";
}
