using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class PasswordResetOutboxGenerationCancellationSqlServerIntegrationTests
{
    private const string OldPassword = "Old Password 987!";
    private const string NewPassword = "New Password 987!";
    private const string Purpose = "ResetPassword";

    [SqlServerIntegrationFact]
    public async Task ScenarioA_ResetCancelsPendingMessageAndPreservesItsHistory()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var user = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, user.Id);
        var pendingBefore = await ReadSingleMessageAsync(database, user.Id);
        var token = await GenerateTokenAsync(database, user.Id);

        await using var scope = CreateScope(database);
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<XaquiamuniDbContext>();
        var trackedMessage = await dbContext.EmailOutboxMessages
            .SingleAsync(message => message.Id == pendingBefore.Id);
        var result = await services.GetRequiredService<IPasswordResetService>()
            .ResetPasswordAsync(token.Email, token.Token, NewPassword);

        Assert.Equal(PasswordResetStatus.Succeeded, result.Status);
        Assert.Equal(EntityState.Detached, dbContext.Entry(trackedMessage).State);
        await dbContext.SaveChangesAsync();

        var cancelled = await ReadMessageAsync(database, pendingBefore.Id);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ActiveDeduplicationKey);
        Assert.Null(cancelled.LeaseExpiresAtUtc);
        Assert.Null(cancelled.CompletedAtUtc);
        Assert.Equal(pendingBefore.Attempts, cancelled.Attempts);
        Assert.True(SameBytes(
            pendingBefore.CredentialGenerationFingerprint,
            cancelled.CredentialGenerationFingerprint));
        Assert.False(SameBytes(pendingBefore.RowVersion, cancelled.RowVersion));
        Assert.True(SameImmutableHistory(pendingBefore, cancelled));
        Assert.True(await CheckPasswordAsync(database, user.Id, NewPassword));
        Assert.False(await VerifyTokenAsync(database, user.Id, token.Token));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioB_ResetCancelsProcessingMessageAndInvalidatesItsOldClaim()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var user = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, user.Id);
        var token = await GenerateTokenAsync(database, user.Id);

        await using var scope = CreateScope(database);
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IEmailOutboxStore>();
        var claim = Assert.Single(await store.ClaimDueAsync(
            EmailOutboxMessageType.PasswordReset,
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(2)));
        var processingBefore = await ReadMessageAsync(database, claim.Id);

        var result = await services.GetRequiredService<IPasswordResetService>()
            .ResetPasswordAsync(token.Email, token.Token, NewPassword);
        var cancelled = await ReadMessageAsync(database, claim.Id);

        Assert.Equal(PasswordResetStatus.Succeeded, result.Status);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ActiveDeduplicationKey);
        Assert.Null(cancelled.LeaseExpiresAtUtc);
        Assert.Equal(processingBefore.Attempts, cancelled.Attempts);
        Assert.True(SameBytes(
            processingBefore.CredentialGenerationFingerprint,
            cancelled.CredentialGenerationFingerprint));
        Assert.False(SameBytes(processingBefore.RowVersion, cancelled.RowVersion));
        Assert.False(await store.CompleteAsync(claim));
        Assert.False(await store.RetryAsync(claim, jitterSample: 0.5));
        Assert.False(await store.CancelAsync(claim));
        Assert.False(await store.DeadLetterAsync(claim));
        Assert.Empty(await store.ClaimDueAsync(
            EmailOutboxMessageType.PasswordReset,
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(2)));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioC_ResetChangesOnlyActiveStalePasswordResetMessages()
    {
        var sameGenerationSeed = new SeedCurrentGenerationMessageInterceptor();
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dbContextInterceptor: sameGenerationSeed);
        var user = await CreateConfirmedUserAsync(database);
        var otherUser = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, user.Id);
        await RequestRecoveryAsync(database, otherUser.Id);
        var oldPending = await ReadSingleMessageAsync(database, user.Id);
        var oldFingerprint = oldPending.CredentialGenerationFingerprint!;
        var historicalMessages = await SeedHistoricalMessagesAsync(database, user.Id, oldFingerprint);
        var expiredProcessingBefore = await SeedExpiredProcessingMessageAsync(
            database,
            user.Id,
            oldFingerprint);
        var token = await GenerateTokenAsync(database, user.Id);

        await using (var seedScope = CreateScope(database))
        {
            sameGenerationSeed.Arm(
                user.Id,
                seedScope.ServiceProvider.GetRequiredService<ICredentialGenerationFingerprint>());
        }

        await using (var scope = CreateScope(database))
        {
            var result = await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
                .ResetPasswordAsync(token.Email, token.Token, NewPassword);
            Assert.Equal(PasswordResetStatus.Succeeded, result.Status);
        }

        var after = await ReadMessagesAsync(database);
        var afterById = after.ToDictionary(message => message.Id);
        var cancelledPending = afterById[oldPending.Id];
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelledPending.Status);
        Assert.Null(cancelledPending.ActiveDeduplicationKey);
        Assert.True(SameBytes(oldFingerprint, cancelledPending.CredentialGenerationFingerprint));
        Assert.False(SameBytes(oldPending.RowVersion, cancelledPending.RowVersion));

        var cancelledProcessing = afterById[expiredProcessingBefore.Id];
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelledProcessing.Status);
        Assert.Null(cancelledProcessing.ActiveDeduplicationKey);
        Assert.Null(cancelledProcessing.LeaseExpiresAtUtc);
        Assert.Equal(expiredProcessingBefore.Attempts, cancelledProcessing.Attempts);
        Assert.True(SameBytes(
            expiredProcessingBefore.CredentialGenerationFingerprint,
            cancelledProcessing.CredentialGenerationFingerprint));
        Assert.False(SameBytes(expiredProcessingBefore.RowVersion, cancelledProcessing.RowVersion));
        Assert.True(SameImmutableHistory(expiredProcessingBefore, cancelledProcessing));

        foreach (var historical in historicalMessages)
        {
            Assert.True(SameSnapshot(historical, afterById[historical.Id]));
        }

        var otherUsersMessage = Assert.Single(after, message => message.UserId == otherUser.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, otherUsersMessage.Status);

        var confirmation = Assert.Single(after, message =>
            message.UserId == user.Id
            && message.MessageType == EmailOutboxMessageType.EmailConfirmation);
        Assert.Equal(EmailOutboxMessageStatus.Pending, confirmation.Status);
        Assert.Null(confirmation.CredentialGenerationFingerprint);

        var currentGenerationMessage = await ReadMessageAsync(database, sameGenerationSeed.MessageId);
        var currentUser = await ReadUserAsync(database, user.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, currentGenerationMessage.Status);
        Assert.True(await FingerprintMatches(
            database,
            currentUser.SecurityStamp,
            currentGenerationMessage.CredentialGenerationFingerprint));
        Assert.NotNull(currentGenerationMessage.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioD_RejectedResetLeavesPasswordAndOutboxUnchanged()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var user = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, user.Id);
        var token = await GenerateTokenAsync(database, user.Id);
        var userBefore = await ReadUserAsync(database, user.Id);
        var messageBefore = await ReadSingleMessageAsync(database, user.Id);

        var result = await ResetPasswordAsync(database, token.Email, token.Token, "weak");

        var userAfter = await ReadUserAsync(database, user.Id);
        var messageAfter = await ReadSingleMessageAsync(database, user.Id);
        Assert.Equal(PasswordResetStatus.InvalidPassword, result.Status);
        Assert.True(SameUserState(userBefore, userAfter));
        Assert.True(SameSnapshot(messageBefore, messageAfter));
        Assert.True(await CheckPasswordAsync(database, user.Id, OldPassword));
        Assert.True(await VerifyTokenAsync(database, user.Id, token.Token));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioE_FailureAfterOutboxUpdateRollsBackAndDoesNotPersistTrackedState()
    {
        var commitInterceptor = new FailCommitAfterCancellationInterceptor();
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dbContextInterceptor: commitInterceptor);
        var user = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, user.Id);
        var token = await GenerateTokenAsync(database, user.Id);
        var userBefore = await ReadUserAsync(database, user.Id);
        var messageBefore = await ReadSingleMessageAsync(database, user.Id);

        await using var scope = CreateScope(database);
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<XaquiamuniDbContext>();
        var trackedMessage = await dbContext.EmailOutboxMessages
            .SingleAsync(message => message.Id == messageBefore.Id);
        commitInterceptor.Arm(messageBefore.Id);

        await Assert.ThrowsAsync<InjectedCommitFailureException>(
            () => services.GetRequiredService<IPasswordResetService>()
                .ResetPasswordAsync(token.Email, token.Token, NewPassword));

        Assert.True(commitInterceptor.CancellationWasVisibleBeforeCommit);
        Assert.Equal(EntityState.Detached, dbContext.Entry(trackedMessage).State);
        var userManager = services.GetRequiredService<UserManager<XaquiamuniUser>>();
        var trackedUser = await userManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(trackedUser);
        Assert.True(SameUserState(userBefore, ToUserState(trackedUser)));
        await dbContext.SaveChangesAsync();

        var userAfter = await ReadUserAsync(database, user.Id);
        var messageAfter = await ReadMessageAsync(database, messageBefore.Id);
        Assert.True(SameUserState(userBefore, userAfter));
        Assert.True(SameSnapshot(messageBefore, messageAfter));
        Assert.True(await CheckPasswordAsync(database, user.Id, OldPassword));
        Assert.True(await VerifyTokenAsync(database, user.Id, token.Token));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioF_NewRequestUsesNewFingerprintAndPreservesCooldownAndHourlyHistory()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var user = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, user.Id);
        var oldPending = await ReadSingleMessageAsync(database, user.Id);
        await SeedHistoryForRateLimitAsync(
            database,
            user.Id,
            oldPending.CredentialGenerationFingerprint!);
        var token = await GenerateTokenAsync(database, user.Id);

        var reset = await ResetPasswordAsync(database, token.Email, token.Token, NewPassword);
        Assert.Equal(PasswordResetStatus.Succeeded, reset.Status);
        var cancelledOld = await ReadMessageAsync(database, oldPending.Id);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelledOld.Status);
        Assert.True(SameValue(oldPending.CreatedAtUtc, cancelledOld.CreatedAtUtc));

        await MakeRequestHistoryOutsideCooldownAsync(database, user.Id);
        Assert.Equal(
            PasswordResetOutboxResult.Enqueued,
            await RequestRecoveryAsync(database, user.Id));

        var pending = Assert.Single(await ReadMessagesAsync(database, user.Id),
            message => message.Status == EmailOutboxMessageStatus.Pending);
        var currentUser = await ReadUserAsync(database, user.Id);
        Assert.True(await FingerprintMatches(
            database,
            currentUser.SecurityStamp,
            pending.CredentialGenerationFingerprint));
        Assert.True(SameBytes(
            oldPending.CredentialGenerationFingerprint,
            cancelledOld.CredentialGenerationFingerprint));

        await using var scope = CreateScope(database);
        var store = scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>();
        var claim = Assert.Single(await store.ClaimDueAsync(
            EmailOutboxMessageType.PasswordReset,
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(2)));
        Assert.True(await store.CancelAsync(claim));
        Assert.Equal(
            PasswordResetOutboxResult.HourlyLimitReached,
            await RequestRecoveryAsync(database, user.Id));
        Assert.Equal(5, (await ReadMessagesAsync(database, user.Id)).Count);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioG_CancellationRequiresAndUsesTheCurrentTransaction()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var user = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, user.Id);
        await UpdateSecurityStampAsync(database, user.Id);
        var before = await ReadSingleMessageAsync(database, user.Id);

        await using var scope = CreateScope(database);
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<XaquiamuniDbContext>();
        var cancellation = services.GetRequiredService<IPasswordResetOutboxGenerationCancellation>();
        var userManager = services.GetRequiredService<UserManager<XaquiamuniUser>>();
        var trackedUser = await userManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(trackedUser);
        var fingerprintService = services.GetRequiredService<ICredentialGenerationFingerprint>();
        var newFingerprint = fingerprintService.Compute(trackedUser.SecurityStamp!);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => cancellation.CancelStaleMessagesAsync(
                    user.Id,
                    newFingerprint,
                    CancellationToken.None));
            Assert.True(SameSnapshot(before, await ReadMessageAsync(database, before.Id)));

            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var connection = Assert.IsType<SqlConnection>(dbContext.Database.GetDbConnection());
            var sqlTransaction = Assert.IsType<SqlTransaction>(transaction.GetDbTransaction());
            await PasswordResetGenerationLock.AcquireAsync(
                connection,
                sqlTransaction,
                user.Id,
                CancellationToken.None);

            Assert.Equal(
                1,
                await cancellation.CancelStaleMessagesAsync(
                    user.Id,
                    newFingerprint,
                    CancellationToken.None));
            Assert.NotNull(dbContext.Database.CurrentTransaction);
            var insideTransaction = await dbContext.EmailOutboxMessages
                .AsNoTracking()
                .SingleAsync(message => message.Id == before.Id);
            Assert.Equal(EmailOutboxMessageStatus.Cancelled, insideTransaction.Status);
            await transaction.RollbackAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newFingerprint);
        }

        Assert.True(SameSnapshot(before, await ReadMessageAsync(database, before.Id)));
    }

    private static async Task<TestUser> CreateConfirmedUserAsync(SqlServerTestDatabase database)
    {
        await using var scope = CreateScope(database);
        var email = $"password-reset-{Guid.NewGuid():N}@example.test";
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var result = await scope.ServiceProvider
            .GetRequiredService<UserManager<XaquiamuniUser>>()
            .CreateAsync(user, OldPassword);
        Assert.True(result.Succeeded);
        return new TestUser(user.Id, email);
    }

    private static async Task<PasswordResetOutboxResult> RequestRecoveryAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var scope = CreateScope(database);
        return await scope.ServiceProvider.GetRequiredService<IPasswordResetOutbox>()
            .RequestAsync(userId);
    }

    private static async Task<ResetToken> GenerateTokenAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var scope = CreateScope(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var fingerprint = scope.ServiceProvider
            .GetRequiredService<ICredentialGenerationFingerprint>()
            .Compute(user.SecurityStamp!);
        try
        {
            var token = await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
                .GeneratePasswordResetTokenForUserAsync(userId, fingerprint);
            Assert.Equal(PasswordResetTokenStatus.Generated, token.Status);
            return new ResetToken(token.Email!, token.Token!);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fingerprint);
        }
    }

    private static async Task<PasswordResetResult> ResetPasswordAsync(
        SqlServerTestDatabase database,
        string email,
        string token,
        string newPassword)
    {
        await using var scope = CreateScope(database);
        return await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
            .ResetPasswordAsync(email, token, newPassword);
    }

    private static async Task<UserState> ReadUserAsync(SqlServerTestDatabase database, Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        var user = await context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
        return ToUserState(user);
    }

    private static UserState ToUserState(XaquiamuniUser user) =>
        new(user.PasswordHash!, user.SecurityStamp!, user.ConcurrencyStamp!, user.EmailConfirmed);

    private static async Task UpdateSecurityStampAsync(SqlServerTestDatabase database, Guid userId)
    {
        await using var scope = CreateScope(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var result = await userManager.UpdateSecurityStampAsync(user);
        Assert.True(result.Succeeded);
    }

    private static async Task<bool> CheckPasswordAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string password)
    {
        await using var scope = CreateScope(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await userManager.CheckPasswordAsync(user, password);
    }

    private static async Task<bool> VerifyTokenAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string token)
    {
        await using var scope = CreateScope(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await userManager.VerifyUserTokenAsync(
            user,
            XaquiamuniPasswordResetTokenProvider.ProviderName,
            Purpose,
            token);
    }

    private static async Task<OutboxSnapshot> ReadSingleMessageAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        var message = await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(candidate =>
                candidate.UserId == userId
                && candidate.MessageType == EmailOutboxMessageType.PasswordReset);
        return ToSnapshot(message);
    }

    private static async Task<OutboxSnapshot> ReadMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        var message = await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == messageId);
        return ToSnapshot(message);
    }

    private static async Task<List<OutboxSnapshot>> ReadMessagesAsync(
        SqlServerTestDatabase database,
        Guid? userId = null)
    {
        await using var context = database.CreateVerificationContext();
        var messages = await context.EmailOutboxMessages.AsNoTracking()
            .Where(message => userId == null || message.UserId == userId)
            .ToListAsync();
        return messages.Select(ToSnapshot).ToList();
    }

    private static OutboxSnapshot ToSnapshot(EmailOutboxMessage message) =>
        new(
            message.Id,
            message.UserId,
            message.MessageType,
            message.Status,
            message.Attempts,
            message.CreatedAtUtc,
            message.NextAttemptAtUtc,
            message.CompletedAtUtc,
            message.ActiveDeduplicationKey,
            message.LeaseExpiresAtUtc,
            message.CredentialGenerationFingerprint,
            (byte[])message.RowVersion.Clone());

    private static async Task<List<OutboxSnapshot>> SeedHistoricalMessagesAsync(
        SqlServerTestDatabase database,
        Guid userId,
        byte[] oldFingerprint)
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[]
        {
            CreateMessage(
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.Completed,
                attempts: 2,
                now.AddMinutes(-30),
                completedAtUtc: now.AddMinutes(-25),
                activeDeduplicationKey: null,
                oldFingerprint),
            CreateMessage(
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.Cancelled,
                attempts: 3,
                now.AddMinutes(-20),
                completedAtUtc: null,
                activeDeduplicationKey: null,
                oldFingerprint),
            CreateMessage(
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.DeadLetter,
                attempts: 5,
                now.AddMinutes(-10),
                completedAtUtc: null,
                activeDeduplicationKey: $"historical-dead-letter:{Guid.NewGuid():N}",
                oldFingerprint),
            CreateMessage(
                userId,
                EmailOutboxMessageType.EmailConfirmation,
                EmailOutboxMessageStatus.Pending,
                attempts: 0,
                now.AddMinutes(-5),
                completedAtUtc: null,
                activeDeduplicationKey: $"email-confirmation:{userId:N}",
                credentialGenerationFingerprint: null)
        };

        foreach (var message in messages)
        {
            await SeedMessageAsync(database, message);
        }

        var snapshots = new List<OutboxSnapshot>();
        foreach (var message in messages)
        {
            snapshots.Add(await ReadMessageAsync(database, message.Id));
        }

        return snapshots;
    }

    private static async Task<OutboxSnapshot> SeedExpiredProcessingMessageAsync(
        SqlServerTestDatabase database,
        Guid userId,
        byte[] oldFingerprint)
    {
        var message = CreateMessage(
            userId,
            EmailOutboxMessageType.PasswordReset,
            EmailOutboxMessageStatus.Processing,
            attempts: 1,
            DateTimeOffset.UtcNow.AddMinutes(-40),
            completedAtUtc: null,
            activeDeduplicationKey: $"password-reset-processing:{Guid.NewGuid():N}",
            oldFingerprint);
        await SeedMessageAsync(database, message);
        return await ReadMessageAsync(database, message.Id);
    }

    private static async Task SeedHistoryForRateLimitAsync(
        SqlServerTestDatabase database,
        Guid userId,
        byte[] oldFingerprint)
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[]
        {
            CreateMessage(
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.Completed,
                attempts: 1,
                now.AddMinutes(-20),
                completedAtUtc: now.AddMinutes(-19),
                activeDeduplicationKey: null,
                oldFingerprint),
            CreateMessage(
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.Cancelled,
                attempts: 2,
                now.AddMinutes(-15),
                completedAtUtc: null,
                activeDeduplicationKey: null,
                oldFingerprint),
            CreateMessage(
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.DeadLetter,
                attempts: 5,
                now.AddMinutes(-10),
                completedAtUtc: null,
                activeDeduplicationKey: $"historical-dead-letter:{Guid.NewGuid():N}",
                oldFingerprint)
        };

        foreach (var message in messages)
        {
            await SeedMessageAsync(database, message);
        }
    }

    private static EmailOutboxMessage CreateMessage(
        Guid userId,
        EmailOutboxMessageType messageType,
        EmailOutboxMessageStatus status,
        int attempts,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc,
        string? activeDeduplicationKey,
        byte[]? credentialGenerationFingerprint)
    {
        DateTimeOffset? leaseExpiresAtUtc = status == EmailOutboxMessageStatus.Processing
            ? createdAtUtc.AddMinutes(2)
            : null;
        return new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            messageType,
            status,
            attempts,
            createdAtUtc,
            createdAtUtc,
            completedAtUtc,
            activeDeduplicationKey,
            leaseExpiresAtUtc,
            credentialGenerationFingerprint);
    }

    private static async Task SeedMessageAsync(
        SqlServerTestDatabase database,
        EmailOutboxMessage message)
    {
        await using var context = database.CreateVerificationContext();
        context.EmailOutboxMessages.Add(message);
        await context.SaveChangesAsync();
    }

    private static async Task MakeRequestHistoryOutsideCooldownAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[EmailOutbox]
            SET [CreatedAtUtc] = DATEADD(
                minute,
                -3,
                TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'))
            WHERE [UserId] = {userId}
                AND [MessageType] = {(short)EmailOutboxMessageType.PasswordReset};
            """);
    }

    private static async Task<bool> FingerprintMatches(
        SqlServerTestDatabase database,
        string securityStamp,
        byte[]? fingerprint)
    {
        if (fingerprint is null)
        {
            return false;
        }

        await using var scope = CreateScope(database);
        return scope.ServiceProvider.GetRequiredService<ICredentialGenerationFingerprint>()
            .Matches(securityStamp, fingerprint);
    }

    private static bool SameUserState(UserState first, UserState second) =>
        SameValue(first.PasswordHash, second.PasswordHash)
        && SameValue(first.SecurityStamp, second.SecurityStamp)
        && SameValue(first.ConcurrencyStamp, second.ConcurrencyStamp)
        && first.EmailConfirmed == second.EmailConfirmed;

    private static bool SameSnapshot(OutboxSnapshot first, OutboxSnapshot second) =>
        first.Id == second.Id
        && first.UserId == second.UserId
        && first.MessageType == second.MessageType
        && first.Status == second.Status
        && first.Attempts == second.Attempts
        && SameValue(first.CreatedAtUtc, second.CreatedAtUtc)
        && SameValue(first.NextAttemptAtUtc, second.NextAttemptAtUtc)
        && SameValue(first.CompletedAtUtc, second.CompletedAtUtc)
        && SameValue(first.ActiveDeduplicationKey, second.ActiveDeduplicationKey)
        && SameValue(first.LeaseExpiresAtUtc, second.LeaseExpiresAtUtc)
        && SameBytes(first.CredentialGenerationFingerprint, second.CredentialGenerationFingerprint)
        && SameBytes(first.RowVersion, second.RowVersion);

    private static bool SameImmutableHistory(OutboxSnapshot before, OutboxSnapshot after) =>
        before.Id == after.Id
        && before.UserId == after.UserId
        && before.MessageType == after.MessageType
        && before.Attempts == after.Attempts
        && SameValue(before.CreatedAtUtc, after.CreatedAtUtc)
        && SameValue(before.NextAttemptAtUtc, after.NextAttemptAtUtc);

    private static bool SameBytes(byte[]? first, byte[]? second) =>
        first is null
            ? second is null
            : second is not null && first.AsSpan().SequenceEqual(second);

    private static bool SameValue<T>(T? first, T? second) =>
        EqualityComparer<T?>.Default.Equals(first, second);

    private static AsyncServiceScope CreateScope(SqlServerTestDatabase database) =>
        database.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

    private sealed record TestUser(Guid Id, string Email);
    private sealed record ResetToken(string Email, string Token);
    private sealed record UserState(
        string PasswordHash,
        string SecurityStamp,
        string ConcurrencyStamp,
        bool EmailConfirmed);
    private sealed record OutboxSnapshot(
        Guid Id,
        Guid UserId,
        EmailOutboxMessageType MessageType,
        EmailOutboxMessageStatus Status,
        int Attempts,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset NextAttemptAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string? ActiveDeduplicationKey,
        DateTimeOffset? LeaseExpiresAtUtc,
        byte[]? CredentialGenerationFingerprint,
        byte[] RowVersion);

    private sealed class SeedCurrentGenerationMessageInterceptor : SaveChangesInterceptor
    {
        private ICredentialGenerationFingerprint? _fingerprint;
        private Guid _userId;
        private int _armed;

        public Guid MessageId { get; private set; }

        public void Arm(Guid userId, ICredentialGenerationFingerprint fingerprint)
        {
            _userId = userId;
            _fingerprint = fingerprint;
            MessageId = Guid.NewGuid();
            Interlocked.Exchange(ref _armed, 1);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) != 1)
            {
                return result;
            }

            var dbContext = eventData.Context
                ?? throw new InvalidOperationException("The test save interceptor has no DbContext.");
            var user = dbContext.ChangeTracker.Entries<XaquiamuniUser>()
                .Single(entry => entry.Entity.Id == _userId)
                .Entity;
            var fingerprint = (_fingerprint
                ?? throw new InvalidOperationException("The current-generation seed was not configured."))
                .Compute(user.SecurityStamp!);
            try
            {
                var connection = Assert.IsType<SqlConnection>(dbContext.Database.GetDbConnection());
                var transaction = Assert.IsType<SqlTransaction>(
                    dbContext.Database.CurrentTransaction?.GetDbTransaction());
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    DECLARE @NowUtc datetimeoffset(7) =
                        TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
                    INSERT INTO [dbo].[EmailOutbox]
                        ([Id], [UserId], [MessageType], [Status], [Attempts],
                         [CreatedAtUtc], [NextAttemptAtUtc], [CompletedAtUtc],
                         [ActiveDeduplicationKey], [LeaseExpiresAtUtc],
                         [CredentialGenerationFingerprint])
                    VALUES
                        (@Id, @UserId, @MessageType, @PendingStatus, 0,
                         @NowUtc, @NowUtc, NULL,
                         @ActiveDeduplicationKey, NULL,
                         @CredentialGenerationFingerprint);
                    """;
                AddParameter(command, "@Id", SqlDbType.UniqueIdentifier, MessageId);
                AddParameter(command, "@UserId", SqlDbType.UniqueIdentifier, _userId);
                AddParameter(
                    command,
                    "@MessageType",
                    SqlDbType.SmallInt,
                    (short)EmailOutboxMessageType.PasswordReset);
                AddParameter(
                    command,
                    "@PendingStatus",
                    SqlDbType.SmallInt,
                    (short)EmailOutboxMessageStatus.Pending);
                AddParameter(
                    command,
                    "@ActiveDeduplicationKey",
                    SqlDbType.NVarChar,
                    $"password-reset-current:{_userId:N}",
                    size: EmailOutboxMessage.ActiveDeduplicationKeyMaxLength);
                AddParameter(
                    command,
                    "@CredentialGenerationFingerprint",
                    SqlDbType.Binary,
                    fingerprint,
                    size: 32);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(fingerprint);
            }
        }
    }

    private sealed class FailCommitAfterCancellationInterceptor : DbTransactionInterceptor
    {
        private Guid _messageId;
        private int _armed;

        public bool CancellationWasVisibleBeforeCommit { get; private set; }

        public void Arm(Guid messageId)
        {
            _messageId = messageId;
            Interlocked.Exchange(ref _armed, 1);
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) != 1)
            {
                return result;
            }

            var connection = Assert.IsType<SqlConnection>(transaction.Connection);
            var sqlTransaction = Assert.IsType<SqlTransaction>(transaction);
            await using var command = connection.CreateCommand();
            command.Transaction = sqlTransaction;
            command.CommandText = """
                SELECT [Status]
                FROM [dbo].[EmailOutbox]
                WHERE [Id] = @Id;
                """;
            AddParameter(command, "@Id", SqlDbType.UniqueIdentifier, _messageId);
            var status = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            CancellationWasVisibleBeforeCommit =
                status is not null
                && status is not DBNull
                && Convert.ToInt16(status) == (short)EmailOutboxMessageStatus.Cancelled;
            throw new InjectedCommitFailureException();
        }
    }

    private sealed class InjectedCommitFailureException()
        : Exception("Injected failure before transaction commit.")
    {
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
