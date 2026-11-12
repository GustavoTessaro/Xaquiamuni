using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

internal interface IPasswordResetOutboxGenerationCancellation
{
    Task<int> CancelStaleMessagesAsync(
        Guid userId,
        byte[] newGenerationFingerprint,
        CancellationToken cancellationToken);
}

internal sealed class SqlServerPasswordResetOutboxGenerationCancellation(
    XaquiamuniDbContext dbContext) : IPasswordResetOutboxGenerationCancellation
{
    private const string CancelStaleMessagesSql = """
        UPDATE [dbo].[EmailOutbox]
        SET
            [Status] = @CancelledStatus,
            [CompletedAtUtc] = NULL,
            [LeaseExpiresAtUtc] = NULL,
            [ActiveDeduplicationKey] = NULL
        WHERE
            [UserId] = @UserId
            AND [MessageType] = @PasswordResetMessageType
            AND [Status] IN (@PendingStatus, @ProcessingStatus)
            AND [CredentialGenerationFingerprint] <> @NewGenerationFingerprint;
        """;

    private readonly XaquiamuniDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public async Task<int> CancelStaleMessagesAsync(
        Guid userId,
        byte[] newGenerationFingerprint,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(newGenerationFingerprint);
        if (newGenerationFingerprint.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException(
                "The credential generation fingerprint must be 32 bytes.",
                nameof(newGenerationFingerprint));
        }

        var currentTransaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Password reset Outbox cancellation requires the active password reset transaction.");
        var connection = _dbContext.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException(
                "Password reset Outbox cancellation requires the SQL Server provider.");
        var transaction = currentTransaction.GetDbTransaction() as SqlTransaction
            ?? throw new InvalidOperationException(
                "Password reset Outbox cancellation requires the active SQL Server transaction.");
        if (!ReferenceEquals(transaction.Connection, connection)
            || connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "Password reset Outbox cancellation must use the active transaction connection.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        DetachTrackedStaleMessages(userId, newGenerationFingerprint);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CancelStaleMessagesSql;
        AddParameter(command, "@UserId", SqlDbType.UniqueIdentifier, userId);
        AddParameter(
            command,
            "@PasswordResetMessageType",
            SqlDbType.SmallInt,
            (short)EmailOutboxMessageType.PasswordReset);
        AddParameter(
            command,
            "@PendingStatus",
            SqlDbType.SmallInt,
            (short)EmailOutboxMessageStatus.Pending);
        AddParameter(
            command,
            "@ProcessingStatus",
            SqlDbType.SmallInt,
            (short)EmailOutboxMessageStatus.Processing);
        AddParameter(
            command,
            "@CancelledStatus",
            SqlDbType.SmallInt,
            (short)EmailOutboxMessageStatus.Cancelled);
        AddParameter(
            command,
            "@NewGenerationFingerprint",
            SqlDbType.Binary,
            newGenerationFingerprint,
            size: SHA256.HashSizeInBytes);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void DetachTrackedStaleMessages(Guid userId, byte[] newGenerationFingerprint)
    {
        var staleEntries = _dbContext.ChangeTracker
            .Entries<EmailOutboxMessage>()
            .Where(entry =>
                entry.Entity.UserId == userId
                && entry.Entity.MessageType == EmailOutboxMessageType.PasswordReset
                && (entry.Entity.Status is EmailOutboxMessageStatus.Pending
                    or EmailOutboxMessageStatus.Processing)
                && HasDifferentFingerprint(entry.Entity, newGenerationFingerprint))
            .ToArray();

        foreach (var entry in staleEntries)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool HasDifferentFingerprint(
        EmailOutboxMessage message,
        byte[] newGenerationFingerprint)
    {
        var historicalFingerprint = message.CredentialGenerationFingerprint;
        if (historicalFingerprint is null)
        {
            return false;
        }

        try
        {
            return !CryptographicOperations.FixedTimeEquals(
                historicalFingerprint,
                newGenerationFingerprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(historicalFingerprint);
        }
    }

    private static void AddParameter(
        SqlCommand command,
        string name,
        SqlDbType type,
        object value,
        int? size = null)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size is not null)
        {
            parameter.Size = size.Value;
        }

        parameter.Value = value;
    }
}
