using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class PasswordResetOutboxSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task RequestAsync_RequiresAnExistingConfirmedUser()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var unconfirmedUserId = await CreateUserAsync(database);
        var confirmedUserId = await CreateUserAsync(database, emailConfirmed: true);
        var emptyStampUserId = await CreateUserAsync(database, emailConfirmed: true);
        await using (var updateStamp = database.CreateVerificationContext())
        {
            await updateStamp.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [dbo].[AspNetUsers] SET [SecurityStamp] = N'' WHERE [Id] = {emptyStampUserId}");
        }

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();

        Assert.Equal(
            PasswordResetOutboxResult.NotEligible,
            await outbox.RequestAsync(Guid.NewGuid()));
        Assert.Equal(
            PasswordResetOutboxResult.NotEligible,
            await outbox.RequestAsync(unconfirmedUserId));
        Assert.Equal(
            PasswordResetOutboxResult.NotEligible,
            await outbox.RequestAsync(emptyStampUserId));
        Assert.Equal(
            PasswordResetOutboxResult.Enqueued,
            await outbox.RequestAsync(confirmedUserId));

        await using var context = database.CreateVerificationContext();
        var message = await context.EmailOutboxMessages.AsNoTracking().SingleAsync();
        var user = await context.Users.AsNoTracking().SingleAsync(user => user.Id == confirmedUserId);
        var fingerprint = database.Services.GetRequiredService<ICredentialGenerationFingerprint>();

        Assert.Equal(EmailOutboxMessageType.PasswordReset, message.MessageType);
        Assert.Equal(EmailOutboxMessageStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.Equal($"password-reset:{confirmedUserId:N}", message.ActiveDeduplicationKey);
        Assert.NotNull(message.CredentialGenerationFingerprint);
        Assert.True(fingerprint.Matches(
            user.SecurityStamp!,
            message.CredentialGenerationFingerprint));
        Assert.Equal(TimeSpan.Zero, message.CreatedAtUtc.Offset);
        Assert.Equal(message.CreatedAtUtc, message.NextAttemptAtUtc);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_DoesNotDuplicatePendingOrProcessingMessagesWithExpiredLeases()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var pendingUserId = await CreateUserAsync(database, emailConfirmed: true);
        var processingUserId = await CreateUserAsync(database, emailConfirmed: true);
        var now = DateTimeOffset.UtcNow;
        await SeedMessageAsync(database, CreateMessage(
            pendingUserId,
            EmailOutboxMessageStatus.Pending,
            now.AddMinutes(-10)));
        await SeedMessageAsync(database, CreateMessage(
            processingUserId,
            EmailOutboxMessageStatus.Processing,
            now.AddMinutes(-10),
            leaseExpiresAtUtc: now.AddMinutes(-1)));

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        Assert.Equal(
            PasswordResetOutboxResult.AlreadyQueued,
            await outbox.RequestAsync(pendingUserId));
        Assert.Equal(
            PasswordResetOutboxResult.AlreadyQueued,
            await outbox.RequestAsync(processingUserId));

        await using var context = database.CreateVerificationContext();
        Assert.Equal(2, await context.EmailOutboxMessages.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_AppliesCooldownAndCountsAllStatusesWithinOneHour()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var cooldownUserId = await CreateUserAsync(database, emailConfirmed: true);
        var completedCooldownUserId = await CreateUserAsync(database, emailConfirmed: true);
        var limitedUserId = await CreateUserAsync(database, emailConfirmed: true);
        var now = DateTimeOffset.UtcNow;
        await SeedMessageAsync(database, CreateMessage(
            cooldownUserId,
            EmailOutboxMessageStatus.Cancelled,
            now.AddMinutes(-1)));
        await SeedMessageAsync(database, CreateMessage(
            completedCooldownUserId,
            EmailOutboxMessageStatus.Completed,
            now.AddMinutes(-10),
            completedAtUtc: now.AddMinutes(-1)));
        foreach (var status in new[]
                 {
                     EmailOutboxMessageStatus.Completed,
                     EmailOutboxMessageStatus.Cancelled,
                     EmailOutboxMessageStatus.DeadLetter,
                     EmailOutboxMessageStatus.Completed,
                     EmailOutboxMessageStatus.Cancelled
                 })
        {
            await SeedMessageAsync(database, CreateMessage(
                limitedUserId,
                status,
                now.AddMinutes(-10)));
        }

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        Assert.Equal(
            PasswordResetOutboxResult.CooldownActive,
            await outbox.RequestAsync(cooldownUserId));
        Assert.Equal(
            PasswordResetOutboxResult.CooldownActive,
            await outbox.RequestAsync(completedCooldownUserId));
        Assert.Equal(
            PasswordResetOutboxResult.HourlyLimitReached,
            await outbox.RequestAsync(limitedUserId));
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_AllowsCompletedAndCancelledMessagesAfterCooldown()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var completedUserId = await CreateUserAsync(database, emailConfirmed: true);
        var cancelledUserId = await CreateUserAsync(database, emailConfirmed: true);
        var now = DateTimeOffset.UtcNow;
        await SeedMessageAsync(database, CreateMessage(
            completedUserId,
            EmailOutboxMessageStatus.Completed,
            now.AddMinutes(-10),
            completedAtUtc: now.AddMinutes(-2).AddSeconds(-10)));
        await SeedMessageAsync(database, CreateMessage(
            cancelledUserId,
            EmailOutboxMessageStatus.Cancelled,
            now.AddMinutes(-2).AddSeconds(-10)));

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(completedUserId));
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(cancelledUserId));
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_ReplacesDeadLetterWithoutChangingItsHistory()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        var deadLetter = CreateMessage(
            userId,
            EmailOutboxMessageStatus.DeadLetter,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            attempts: 5);
        await SeedMessageAsync(database, deadLetter);

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(userId));

        await using var context = database.CreateVerificationContext();
        var messages = await context.EmailOutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.CreatedAtUtc)
            .ToListAsync();
        var preservedDeadLetter = Assert.Single(messages, message => message.Id == deadLetter.Id);
        var replacement = Assert.Single(messages, message => message.Id != deadLetter.Id);

        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, preservedDeadLetter.Status);
        Assert.Equal(deadLetter.Attempts, preservedDeadLetter.Attempts);
        Assert.Null(preservedDeadLetter.ActiveDeduplicationKey);
        Assert.Equal(EmailOutboxMessageStatus.Pending, replacement.Status);
        Assert.Equal(0, replacement.Attempts);
        Assert.Equal($"password-reset:{userId:N}", replacement.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_SerializesConcurrentRequestsForOneUser()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        var fingerprint = database.Services.GetRequiredService<ICredentialGenerationFingerprint>();

        await using var firstContext = database.CreateVerificationContext();
        await using var secondContext = database.CreateVerificationContext();
        var firstOutbox = new SqlServerPasswordResetOutbox(firstContext, fingerprint);
        var secondOutbox = new SqlServerPasswordResetOutbox(secondContext, fingerprint);

        var results = await Task.WhenAll(
            firstOutbox.RequestAsync(userId),
            secondOutbox.RequestAsync(userId));

        Assert.Single(results, result => result == PasswordResetOutboxResult.Enqueued);
        Assert.Single(results, result => result == PasswordResetOutboxResult.AlreadyQueued);
        await using var verification = database.CreateVerificationContext();
        Assert.Equal(1, await verification.EmailOutboxMessages.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_SerializesConcurrentRequestsForOneUserWithReadCommittedSnapshot()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await database.EnableReadCommittedSnapshotAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        var fingerprint = database.Services.GetRequiredService<ICredentialGenerationFingerprint>();

        await using var firstContext = database.CreateVerificationContext();
        await using var secondContext = database.CreateVerificationContext();
        var firstOutbox = new SqlServerPasswordResetOutbox(firstContext, fingerprint);
        var secondOutbox = new SqlServerPasswordResetOutbox(secondContext, fingerprint);
        var results = await Task.WhenAll(
            firstOutbox.RequestAsync(userId),
            secondOutbox.RequestAsync(userId));

        Assert.Single(results, result => result == PasswordResetOutboxResult.Enqueued);
        Assert.Single(results, result => result == PasswordResetOutboxResult.AlreadyQueued);
        await using var verification = database.CreateVerificationContext();
        Assert.Equal(1, await verification.EmailOutboxMessages.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_CountsARequestOnceRegardlessOfRetryAttempts()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        await SeedMessageAsync(database, CreateMessage(
            userId,
            EmailOutboxMessageStatus.Cancelled,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            attempts: 5));

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(userId));

        await using var context = database.CreateVerificationContext();
        Assert.Equal(2, await context.EmailOutboxMessages.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_RollsBackDeadLetterReleaseWhenInsertFails()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        var deadLetter = CreateMessage(
            userId,
            EmailOutboxMessageStatus.DeadLetter,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            attempts: 5);
        await SeedMessageAsync(database, deadLetter);
        await using (var context = database.CreateVerificationContext())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_EmailOutbox_RejectPasswordResetInsert]
                ON [dbo].[EmailOutbox]
                AFTER INSERT
                AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE [MessageType] = 2)
                    BEGIN
                        THROW 51001, 'Injected password reset Outbox insert failure.', 1;
                    END;
                END;
                """);
        }

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        await Assert.ThrowsAsync<SqlException>(() => outbox.RequestAsync(userId));

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.Id == deadLetter.Id);
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, persisted.Status);
        Assert.Equal(deadLetter.Attempts, persisted.Attempts);
        Assert.Equal(deadLetter.ActiveDeduplicationKey, persisted.ActiveDeduplicationKey);
        Assert.Equal(1, await verification.EmailOutboxMessages.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_EnqueuesForDifferentUsersIndependently()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var firstUserId = await CreateUserAsync(database, emailConfirmed: true);
        var secondUserId = await CreateUserAsync(database, emailConfirmed: true);
        var fingerprint = database.Services.GetRequiredService<ICredentialGenerationFingerprint>();

        await using var firstContext = database.CreateVerificationContext();
        await using var secondContext = database.CreateVerificationContext();
        var results = await Task.WhenAll(
            new SqlServerPasswordResetOutbox(firstContext, fingerprint).RequestAsync(firstUserId),
            new SqlServerPasswordResetOutbox(secondContext, fingerprint).RequestAsync(secondUserId));

        Assert.All(results, result => Assert.Equal(PasswordResetOutboxResult.Enqueued, result));
        await using var verification = database.CreateVerificationContext();
        Assert.Equal(2, await verification.EmailOutboxMessages.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task CredentialFingerprint_RemainsStableAcrossClaimRetryReclaimCompletionAndCancellation()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var retryUserId = await CreateUserAsync(database, emailConfirmed: true);
        var cancelUserId = await CreateUserAsync(database, emailConfirmed: true);
        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(retryUserId));
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(cancelUserId));

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);
        var initialClaims = await store.ClaimDueAsync(
            EmailOutboxMessageType.PasswordReset,
            batchSize: 2,
            TimeSpan.FromMinutes(5));
        var retryClaim = Assert.Single(initialClaims, claim => claim.UserId == retryUserId);
        var cancelClaim = Assert.Single(initialClaims, claim => claim.UserId == cancelUserId);
        var retryFingerprint = retryClaim.CredentialGenerationFingerprint!.Value.ToArray();
        var cancelFingerprint = cancelClaim.CredentialGenerationFingerprint!.Value.ToArray();

        Assert.True(await store.RetryAsync(retryClaim, jitterSample: 1));
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [dbo].[EmailOutbox] SET [NextAttemptAtUtc] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') WHERE [Id] = {retryClaim.Id}");
        var reclaimed = Assert.Single(await store.ClaimDueAsync(
            EmailOutboxMessageType.PasswordReset,
            batchSize: 1,
            TimeSpan.FromMinutes(5)));
        Assert.Equal(retryClaim.Id, reclaimed.Id);
        Assert.Equal(2, reclaimed.Attempts);
        Assert.Equal(retryFingerprint, reclaimed.CredentialGenerationFingerprint!.Value.ToArray());
        Assert.True(await store.CompleteAsync(reclaimed));
        Assert.True(await store.CancelAsync(cancelClaim));

        var persisted = await context.EmailOutboxMessages
            .AsNoTracking()
            .ToDictionaryAsync(message => message.UserId);
        Assert.Equal(
            retryFingerprint,
            persisted[retryUserId].CredentialGenerationFingerprint);
        Assert.Equal(
            cancelFingerprint,
            persisted[cancelUserId].CredentialGenerationFingerprint);
        Assert.Equal(EmailOutboxMessageStatus.Completed, persisted[retryUserId].Status);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, persisted[cancelUserId].Status);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_CapturesTheCurrentGenerationAfterSecurityStampChanges()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        var fingerprintService = database.Services.GetRequiredService<ICredentialGenerationFingerprint>();
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(userId));

        byte[] oldFingerprint;
        await using (var context = database.CreateVerificationContext())
        {
            var oldMessage = await context.EmailOutboxMessages.AsNoTracking().SingleAsync();
            oldFingerprint = oldMessage.CredentialGenerationFingerprint!;
        }

        var userManager = database.Services.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        Assert.True((await userManager.UpdateSecurityStampAsync(user)).Succeeded);
        var newSecurityStamp = user.SecurityStamp!;

        await using (var context = database.CreateVerificationContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @CompletedAtUtc datetimeoffset(7) =
                    DATEADD(minute, -3, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
                UPDATE [dbo].[EmailOutbox]
                SET [Status] = {(short)EmailOutboxMessageStatus.Completed},
                    [CreatedAtUtc] = DATEADD(minute, -10, @CompletedAtUtc),
                    [NextAttemptAtUtc] = DATEADD(minute, -10, @CompletedAtUtc),
                    [CompletedAtUtc] = @CompletedAtUtc,
                    [ActiveDeduplicationKey] = NULL,
                    [LeaseExpiresAtUtc] = NULL
                WHERE [UserId] = {userId}
                    AND [MessageType] = {(short)EmailOutboxMessageType.PasswordReset};
                """);
        }

        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(userId));

        await using var verification = database.CreateVerificationContext();
        var messages = await verification.EmailOutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.CreatedAtUtc)
            .ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(oldFingerprint, messages[0].CredentialGenerationFingerprint);
        Assert.True(fingerprintService.Matches(
            newSecurityStamp,
            messages[1].CredentialGenerationFingerprint!));
        Assert.NotEqual(
            messages[0].CredentialGenerationFingerprint,
            messages[1].CredentialGenerationFingerprint);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_LockTimeoutFailsExplicitlyWithoutEnqueueing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        await using var lockContext = database.CreateVerificationContext();
        var connection = (Microsoft.Data.SqlClient.SqlConnection)lockContext.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (Microsoft.Data.SqlClient.SqlTransaction)transaction;
            command.CommandText = """
                DECLARE @LockResult int;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 0,
                    @DbPrincipal = 'public';
                SELECT @LockResult;
                """;
            command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter(
                "@Resource",
                System.Data.SqlDbType.NVarChar,
                255)
            {
                Value = $"PasswordResetGeneration:{userId:N}"
            });
            Assert.True((int)(await command.ExecuteScalarAsync())! >= 0);
        }

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => outbox.RequestAsync(userId));
        Assert.Contains("lock could not be acquired", exception.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync();

        await using var verification = database.CreateVerificationContext();
        Assert.Empty(await verification.EmailOutboxMessages.ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_UsesPasswordResetHistoryAndDeduplicationIndependently()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database, emailConfirmed: true);
        var confirmation = CreateMessage(
            userId,
            EmailOutboxMessageStatus.Pending,
            DateTimeOffset.UtcNow,
            messageType: EmailOutboxMessageType.EmailConfirmation,
            activeDeduplicationKey: $"email-confirmation:{userId:N}");
        await SeedMessageAsync(database, confirmation);

        var outbox = database.Services.GetRequiredService<IPasswordResetOutbox>();
        Assert.Equal(PasswordResetOutboxResult.Enqueued, await outbox.RequestAsync(userId));

        await using var context = database.CreateVerificationContext();
        var messages = await context.EmailOutboxMessages.AsNoTracking().ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(
            new[] { "email-confirmation:", "password-reset:" },
            messages.Select(message => message.ActiveDeduplicationKey!.Split(':')[0] + ":")
                .OrderBy(key => key)
                .ToArray());
    }

    private static async Task<Guid> CreateUserAsync(
        SqlServerTestDatabase database,
        bool emailConfirmed = false)
    {
        var email = $"password-reset-outbox-{Guid.NewGuid():N}@example.test";
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed
        };
        var result = await database.Services
            .GetRequiredService<UserManager<XaquiamuniUser>>()
            .CreateAsync(user, "Password Reset Outbox 987!");
        Assert.True(result.Succeeded);
        return user.Id;
    }

    private static EmailOutboxMessage CreateMessage(
        Guid userId,
        EmailOutboxMessageStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? leaseExpiresAtUtc = null,
        int attempts = 1,
        EmailOutboxMessageType messageType = EmailOutboxMessageType.PasswordReset,
        string? activeDeduplicationKey = null,
        DateTimeOffset? completedAtUtc = null)
    {
        var deduplicationKey = activeDeduplicationKey
            ?? (status is EmailOutboxMessageStatus.Pending
                or EmailOutboxMessageStatus.Processing
                or EmailOutboxMessageStatus.DeadLetter
                    ? $"password-reset:{userId:N}"
                    : null);
        return new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            messageType,
            status,
            attempts,
            createdAtUtc,
            createdAtUtc,
            completedAtUtc: status == EmailOutboxMessageStatus.Completed
                ? completedAtUtc ?? createdAtUtc
                : null,
            activeDeduplicationKey: deduplicationKey,
            leaseExpiresAtUtc: leaseExpiresAtUtc,
            credentialGenerationFingerprint: messageType == EmailOutboxMessageType.PasswordReset
                ? new byte[32]
                : null);
    }

    private static async Task SeedMessageAsync(
        SqlServerTestDatabase database,
        EmailOutboxMessage message)
    {
        await using var context = database.CreateVerificationContext();
        context.EmailOutboxMessages.Add(message);
        await context.SaveChangesAsync();
    }
}
