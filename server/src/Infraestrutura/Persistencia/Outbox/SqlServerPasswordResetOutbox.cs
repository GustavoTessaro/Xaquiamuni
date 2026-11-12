using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;

namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

public sealed class SqlServerPasswordResetOutbox : IPasswordResetOutbox
{
    private const int MaximumRequestsPerHour = 5;
    private const int CooldownMinutes = 2;
    private const int MaximumSecurityStampLength = 256;
    private const string DeduplicationKeyPrefix = "password-reset:";

    private const string FindUserSql = """
        SELECT [EmailConfirmed], [SecurityStamp]
        FROM [dbo].[AspNetUsers] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        WHERE [Id] = @UserId;
        """;

    private const string FindActiveMessageSql = """
        SELECT [Id], [UserId], [MessageType], [Status], [RowVersion]
        FROM [dbo].[EmailOutbox] WITH
            (UPDLOCK, HOLDLOCK, ROWLOCK, INDEX(UX_EmailOutbox_ActiveDeduplicationKey))
        WHERE [ActiveDeduplicationKey] = @DeduplicationKey;
        """;

    private const string FindQueuedMessageSql = """
        SELECT TOP (1) [Id]
        FROM [dbo].[EmailOutbox] WITH
            (UPDLOCK, HOLDLOCK, ROWLOCK, INDEX(IX_EmailOutbox_UserId))
        WHERE [UserId] = @UserId
            AND [MessageType] = @MessageType
            AND [Status] IN (@PendingStatus, @ProcessingStatus);
        """;

    private const string ReadRequestHistorySql = """
        DECLARE @NowUtc datetimeoffset(7) =
            TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
        DECLARE @RequestCount bigint;
        DECLARE @LastCreatedAtUtc datetimeoffset(7);
        DECLARE @LastCompletedAtUtc datetimeoffset(7);

        SELECT @RequestCount = COUNT_BIG(*)
        FROM [dbo].[EmailOutbox] WITH
            (UPDLOCK, HOLDLOCK, ROWLOCK, INDEX(IX_EmailOutbox_UserId))
        WHERE [UserId] = @UserId
            AND [MessageType] = @MessageType
            AND [CreatedAtUtc] >= DATEADD(hour, -1, @NowUtc)
            AND [CreatedAtUtc] <= @NowUtc;

        SELECT TOP (1)
            @LastCreatedAtUtc = [CreatedAtUtc],
            @LastCompletedAtUtc = [CompletedAtUtc]
        FROM [dbo].[EmailOutbox] WITH
            (UPDLOCK, HOLDLOCK, ROWLOCK, INDEX(IX_EmailOutbox_UserId))
        WHERE [UserId] = @UserId
            AND [MessageType] = @MessageType
        ORDER BY [CreatedAtUtc] DESC, [Id] DESC;

        SELECT @NowUtc, @RequestCount, @LastCreatedAtUtc, @LastCompletedAtUtc;
        """;

    private const string ReleaseDeadLetterSql = """
        UPDATE [dbo].[EmailOutbox]
        SET [ActiveDeduplicationKey] = NULL
        WHERE [Id] = @Id
            AND [UserId] = @UserId
            AND [MessageType] = @MessageType
            AND [Status] = @DeadLetterStatus
            AND [CompletedAtUtc] IS NULL
            AND [LeaseExpiresAtUtc] IS NULL
            AND [ActiveDeduplicationKey] = @DeduplicationKey
            AND [RowVersion] = @RowVersion;
        """;

    private const string InsertPendingMessageSql = """
        INSERT INTO [dbo].[EmailOutbox]
            ([Id], [UserId], [MessageType], [Status], [Attempts],
             [CreatedAtUtc], [NextAttemptAtUtc], [CompletedAtUtc],
             [ActiveDeduplicationKey], [LeaseExpiresAtUtc],
             [CredentialGenerationFingerprint])
        VALUES
            (@Id, @UserId, @MessageType, @PendingStatus, 0,
             @NowUtc, @NowUtc, NULL,
             @DeduplicationKey, NULL,
             @CredentialGenerationFingerprint);
        """;

    private readonly XaquiamuniDbContext _dbContext;
    private readonly ICredentialGenerationFingerprint _fingerprint;

    public SqlServerPasswordResetOutbox(
        XaquiamuniDbContext dbContext,
        ICredentialGenerationFingerprint fingerprint)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
    }

    public async Task<PasswordResetOutboxResult> RequestAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return PasswordResetOutboxResult.NotEligible;
        }

        var connection = GetSqlConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            await PasswordResetGenerationLock
                .AcquireAsync(connection, transaction, userId, cancellationToken)
                .ConfigureAwait(false);

            var user = await FindUserAsync(connection, transaction, userId, cancellationToken)
                .ConfigureAwait(false);
            if (user is null
                || !user.EmailConfirmed
                || !IsValidSecurityStamp(user.SecurityStamp))
            {
                return await CommitAndReturnAsync(
                    transaction,
                    PasswordResetOutboxResult.NotEligible,
                    cancellationToken).ConfigureAwait(false);
            }

            var fingerprint = _fingerprint.Compute(user.SecurityStamp!);
            var deduplicationKey = $"{DeduplicationKeyPrefix}{userId:N}";
            var queuedMessage = await FindQueuedMessageAsync(
                connection,
                transaction,
                userId,
                cancellationToken).ConfigureAwait(false);
            if (queuedMessage)
            {
                return await CommitAndReturnAsync(
                    transaction,
                    PasswordResetOutboxResult.AlreadyQueued,
                    cancellationToken).ConfigureAwait(false);
            }

            var activeMessage = await FindActiveMessageAsync(
                connection,
                transaction,
                userId,
                deduplicationKey,
                cancellationToken).ConfigureAwait(false);
            if (activeMessage is not null
                && activeMessage.Status != EmailOutboxMessageStatus.DeadLetter)
            {
                throw new InvalidOperationException(
                    "The password reset deduplication key has an invalid Outbox state.");
            }

            var history = await ReadRequestHistoryAsync(
                connection,
                transaction,
                userId,
                cancellationToken).ConfigureAwait(false);
            if (history.RequestCount >= MaximumRequestsPerHour)
            {
                return await CommitAndReturnAsync(
                    transaction,
                    PasswordResetOutboxResult.HourlyLimitReached,
                    cancellationToken).ConfigureAwait(false);
            }

            if (history.LastCreatedAtUtc is DateTimeOffset lastCreatedAtUtc)
            {
                var cooldownAnchor = history.LastCompletedAtUtc is DateTimeOffset lastCompletedAtUtc
                    && lastCompletedAtUtc > lastCreatedAtUtc
                        ? lastCompletedAtUtc
                        : lastCreatedAtUtc;
                if (cooldownAnchor > history.NowUtc.AddMinutes(-CooldownMinutes))
                {
                    return await CommitAndReturnAsync(
                        transaction,
                        PasswordResetOutboxResult.CooldownActive,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (activeMessage is not null)
            {
                await ReleaseDeadLetterAsync(
                    connection,
                    transaction,
                    activeMessage,
                    userId,
                    deduplicationKey,
                    cancellationToken).ConfigureAwait(false);
            }

            await InsertPendingMessageAsync(
                connection,
                transaction,
                userId,
                deduplicationKey,
                fingerprint,
                history.NowUtc,
                cancellationToken).ConfigureAwait(false);

            return await CommitAndReturnAsync(
                transaction,
                PasswordResetOutboxResult.Enqueued,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<ResetUser?> FindUserAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = FindUserSql;
        AddParameter(command, "@UserId", SqlDbType.UniqueIdentifier, userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ResetUser(
            reader.GetBoolean(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task<bool> FindQueuedMessageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = FindQueuedMessageSql;
        AddParameter(command, "@UserId", SqlDbType.UniqueIdentifier, userId);
        AddParameter(command, "@MessageType", SqlDbType.SmallInt, (short)EmailOutboxMessageType.PasswordReset);
        AddParameter(command, "@PendingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Pending);
        AddParameter(command, "@ProcessingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Processing);

        var messageId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return messageId is not null && messageId is not DBNull;
    }

    private static async Task<ActiveMessage?> FindActiveMessageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid userId,
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = FindActiveMessageSql;
        AddParameter(command, "@DeduplicationKey", SqlDbType.NVarChar, deduplicationKey, size: 128);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var message = new ActiveMessage(
            reader.GetGuid(0),
            reader.GetGuid(1),
            (EmailOutboxMessageType)reader.GetInt16(2),
            (EmailOutboxMessageStatus)reader.GetInt16(3),
            (byte[])reader.GetValue(4));
        if (message.UserId != userId || message.MessageType != EmailOutboxMessageType.PasswordReset)
        {
            throw new InvalidOperationException(
                "The password reset deduplication key belongs to a different Outbox message.");
        }

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The password reset deduplication key is not unique.");
        }

        return message;
    }

    private static async Task<RequestHistory> ReadRequestHistoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReadRequestHistorySql;
        AddParameter(command, "@UserId", SqlDbType.UniqueIdentifier, userId);
        AddParameter(command, "@MessageType", SqlDbType.SmallInt, (short)EmailOutboxMessageType.PasswordReset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The password reset request history query returned no result.");
        }

        return new RequestHistory(
            reader.GetDateTimeOffset(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetDateTimeOffset(2),
            reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3));
    }

    private static async Task ReleaseDeadLetterAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ActiveMessage message,
        Guid userId,
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReleaseDeadLetterSql;
        AddParameter(command, "@Id", SqlDbType.UniqueIdentifier, message.Id);
        AddParameter(command, "@UserId", SqlDbType.UniqueIdentifier, userId);
        AddParameter(command, "@MessageType", SqlDbType.SmallInt, (short)EmailOutboxMessageType.PasswordReset);
        AddParameter(command, "@DeadLetterStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.DeadLetter);
        AddParameter(command, "@DeduplicationKey", SqlDbType.NVarChar, deduplicationKey, size: 128);
        AddParameter(command, "@RowVersion", SqlDbType.Binary, message.RowVersion, size: 8);

        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException(
                "The DeadLetter Outbox message changed before its deduplication key could be released.");
        }
    }

    private static async Task InsertPendingMessageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid userId,
        string deduplicationKey,
        byte[] fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertPendingMessageSql;
        AddParameter(command, "@Id", SqlDbType.UniqueIdentifier, Guid.NewGuid());
        AddParameter(command, "@UserId", SqlDbType.UniqueIdentifier, userId);
        AddParameter(command, "@MessageType", SqlDbType.SmallInt, (short)EmailOutboxMessageType.PasswordReset);
        AddParameter(command, "@PendingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Pending);
        AddParameter(command, "@NowUtc", SqlDbType.DateTimeOffset, nowUtc);
        AddParameter(command, "@DeduplicationKey", SqlDbType.NVarChar, deduplicationKey, size: 128);
        AddParameter(
            command,
            "@CredentialGenerationFingerprint",
            SqlDbType.Binary,
            fingerprint,
            size: 32);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqlConnection GetSqlConnection()
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Password reset Outbox requests require their own short SQL transaction.");
        }

        return _dbContext.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException(
                "Password reset Outbox persistence requires the SQL Server provider.");
    }

    private static async Task<T> CommitAndReturnAsync<T>(
        SqlTransaction transaction,
        T result,
        CancellationToken cancellationToken)
    {
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static bool IsValidSecurityStamp(string? securityStamp) =>
        !string.IsNullOrWhiteSpace(securityStamp)
        && securityStamp.Length <= MaximumSecurityStampLength
        && !securityStamp.Any(char.IsControl);

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

    private sealed record ResetUser(bool EmailConfirmed, string? SecurityStamp);

    private sealed record ActiveMessage(
        Guid Id,
        Guid UserId,
        EmailOutboxMessageType MessageType,
        EmailOutboxMessageStatus Status,
        byte[] RowVersion);

    private sealed record RequestHistory(
        DateTimeOffset NowUtc,
        long RequestCount,
        DateTimeOffset? LastCreatedAtUtc,
        DateTimeOffset? LastCompletedAtUtc);
}
