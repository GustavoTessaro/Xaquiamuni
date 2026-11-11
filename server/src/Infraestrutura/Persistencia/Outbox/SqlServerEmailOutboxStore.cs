using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Outbox;

namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

public sealed class SqlServerEmailOutboxStore : IEmailOutboxStore
{
    private const int MinimumBatchSize = 1;
    private const int MaximumBatchSize = 100;
    private static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromDays(1);

    private const string ClaimSql = """
        DECLARE @NowUtc datetimeoffset(7) =
            TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
        DECLARE @LeaseExpiresAtUtc datetimeoffset(7) =
            DATEADD(millisecond, @LeaseMilliseconds, @NowUtc);

        ;WITH EligibleMessages AS
        (
            SELECT TOP (@BatchSize) *
            FROM [dbo].[EmailOutbox] WITH (UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK)
            WHERE
            (
                ([Status] = @PendingStatus AND [NextAttemptAtUtc] <= @NowUtc)
                OR
                ([Status] = @ProcessingStatus AND [LeaseExpiresAtUtc] <= @NowUtc)
            )
            /* EXCLUDED_MESSAGE_IDS */
            ORDER BY
                CASE
                    WHEN [Status] = @PendingStatus THEN [NextAttemptAtUtc]
                    ELSE [LeaseExpiresAtUtc]
                END,
                [CreatedAtUtc],
                [Id]
        )
        UPDATE EligibleMessages
        SET
            [Status] = CASE
                WHEN [Attempts] >= @MaximumAttempts THEN @DeadLetterStatus
                ELSE @ProcessingStatus
            END,
            [Attempts] = CASE
                WHEN [Attempts] >= @MaximumAttempts THEN [Attempts]
                ELSE [Attempts] + 1
            END,
            [LeaseExpiresAtUtc] = CASE
                WHEN [Attempts] >= @MaximumAttempts THEN NULL
                ELSE @LeaseExpiresAtUtc
            END
        OUTPUT
            inserted.[Id],
            inserted.[UserId],
            inserted.[Attempts],
            inserted.[Status],
            inserted.[LeaseExpiresAtUtc],
            inserted.[RowVersion];
        """;

    private const string CompleteSql = """
        DECLARE @NowUtc datetimeoffset(7) =
            TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

        UPDATE [dbo].[EmailOutbox]
        SET
            [Status] = @CompletedStatus,
            [CompletedAtUtc] = @NowUtc,
            [LeaseExpiresAtUtc] = NULL,
            [ActiveDeduplicationKey] = NULL
        WHERE
            [Id] = @Id
            AND [Status] = @ProcessingStatus
            AND [LeaseExpiresAtUtc] > @NowUtc
            AND [RowVersion] = @RowVersion;
        """;

    private const string RetrySql = """
        DECLARE @NowUtc datetimeoffset(7) =
            TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

        UPDATE [dbo].[EmailOutbox]
        SET
            [Status] = @PendingStatus,
            [NextAttemptAtUtc] = DATEADD(millisecond, @DelayMilliseconds, @NowUtc),
            [LeaseExpiresAtUtc] = NULL,
            [CompletedAtUtc] = NULL
        WHERE
            [Id] = @Id
            AND [Status] = @ProcessingStatus
            AND [LeaseExpiresAtUtc] > @NowUtc
            AND [RowVersion] = @RowVersion;
        """;

    private const string CancelSql = """
        DECLARE @NowUtc datetimeoffset(7) =
            TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

        UPDATE [dbo].[EmailOutbox]
        SET
            [Status] = @CancelledStatus,
            [CompletedAtUtc] = NULL,
            [LeaseExpiresAtUtc] = NULL,
            [ActiveDeduplicationKey] = NULL
        WHERE
            [Id] = @Id
            AND [Status] = @ProcessingStatus
            AND [LeaseExpiresAtUtc] > @NowUtc
            AND [RowVersion] = @RowVersion;
        """;

    private const string DeadLetterSql = """
        DECLARE @NowUtc datetimeoffset(7) =
            TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

        UPDATE [dbo].[EmailOutbox]
        SET
            [Status] = @DeadLetterStatus,
            [CompletedAtUtc] = NULL,
            [LeaseExpiresAtUtc] = NULL
        WHERE
            [Id] = @Id
            AND [Status] = @ProcessingStatus
            AND [LeaseExpiresAtUtc] > @NowUtc
            AND [RowVersion] = @RowVersion;
        """;

    private readonly XaquiamuniDbContext _dbContext;

    public SqlServerEmailOutboxStore(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<EmailOutboxClaim>> ClaimDueAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? excludedMessageIds = null)
    {
        ValidateClaimOptions(batchSize, leaseDuration);
        var excludedIds = excludedMessageIds?.Distinct().ToArray() ?? [];
        if (excludedIds.Length > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(excludedMessageIds));
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
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var exclusionPredicate = excludedIds.Length == 0
                ? string.Empty
                : $"AND [Id] NOT IN ({string.Join(", ", Enumerable.Range(0, excludedIds.Length).Select(index => $"@ExcludedId{index}"))})";
            command.CommandText = ClaimSql.Replace(
                "/* EXCLUDED_MESSAGE_IDS */",
                exclusionPredicate,
                StringComparison.Ordinal);
            AddParameter(command, "@BatchSize", SqlDbType.Int, batchSize);
            AddParameter(
                command,
                "@LeaseMilliseconds",
                SqlDbType.Int,
                checked((int)Math.Ceiling(leaseDuration.TotalMilliseconds)));
            AddParameter(command, "@PendingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Pending);
            AddParameter(command, "@ProcessingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Processing);
            AddParameter(command, "@DeadLetterStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.DeadLetter);
            AddParameter(command, "@MaximumAttempts", SqlDbType.Int, EmailOutboxRetryPolicy.MaximumAttempts);
            for (var index = 0; index < excludedIds.Length; index++)
            {
                AddParameter(command, $"@ExcludedId{index}", SqlDbType.UniqueIdentifier, excludedIds[index]);
            }

            var claims = new List<EmailOutboxClaim>(batchSize);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (reader.GetInt16(3) != (short)EmailOutboxMessageStatus.Processing)
                    {
                        continue;
                    }

                    claims.Add(new EmailOutboxClaim(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetInt32(2),
                        reader.GetDateTimeOffset(4),
                        (byte[])reader.GetValue(5)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return claims;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    public Task<bool> CompleteAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return ExecuteConditionalUpdateAsync(
            CompleteSql,
            claim,
            cancellationToken,
            command =>
            {
                AddParameter(command, "@CompletedStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Completed);
                AddParameter(command, "@ProcessingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Processing);
            });
    }

    public Task<bool> RetryAsync(
        EmailOutboxClaim claim,
        double jitterSample,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Attempts >= EmailOutboxRetryPolicy.MaximumAttempts)
        {
            return DeadLetterAsync(claim, cancellationToken);
        }

        var delay = EmailOutboxRetryPolicy.GetRetryDelay(claim.Attempts, jitterSample);
        return ExecuteConditionalUpdateAsync(
            RetrySql,
            claim,
            cancellationToken,
            command =>
            {
                AddParameter(command, "@PendingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Pending);
                AddParameter(command, "@ProcessingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Processing);
                AddParameter(command, "@DelayMilliseconds", SqlDbType.Int, checked((int)delay.TotalMilliseconds));
            });
    }

    public Task<bool> CancelAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return ExecuteConditionalUpdateAsync(
            CancelSql,
            claim,
            cancellationToken,
            command =>
            {
                AddParameter(command, "@CancelledStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Cancelled);
                AddParameter(command, "@ProcessingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Processing);
            });
    }

    public Task<bool> DeadLetterAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return ExecuteConditionalUpdateAsync(
            DeadLetterSql,
            claim,
            cancellationToken,
            command =>
            {
                AddParameter(command, "@DeadLetterStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.DeadLetter);
                AddParameter(command, "@ProcessingStatus", SqlDbType.SmallInt, (short)EmailOutboxMessageStatus.Processing);
            });
    }

    private async Task<bool> ExecuteConditionalUpdateAsync(
        string sql,
        EmailOutboxClaim claim,
        CancellationToken cancellationToken,
        Action<SqlCommand> addOperationParameters)
    {
        var connection = GetSqlConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "@Id", SqlDbType.UniqueIdentifier, claim.Id);
            AddParameter(command, "@RowVersion", SqlDbType.Binary, claim.RowVersion.ToArray(), size: 8);
            addOperationParameters(command);

            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private SqlConnection GetSqlConnection()
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Outbox persistence operations must not run inside a longer database transaction.");
        }

        return _dbContext.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("Email outbox persistence requires the SQL Server provider.");
    }

    private static void ValidateClaimOptions(int batchSize, TimeSpan leaseDuration)
    {
        if (batchSize is < MinimumBatchSize or > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (leaseDuration < TimeSpan.FromMilliseconds(1)
            || leaseDuration > MaximumLeaseDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
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
