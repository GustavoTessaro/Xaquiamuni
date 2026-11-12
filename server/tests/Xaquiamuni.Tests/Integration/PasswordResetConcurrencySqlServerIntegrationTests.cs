using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class PasswordResetConcurrencySqlServerIntegrationTests
{
    private const string OldPassword = "Old Password 987!";
    private const string NewPassword = "New Password 987!";
    private const string PasswordResetPurpose = "ResetPassword";
    private const string SyntheticApiKey = "re_sql_password_reset_test_key";

    [SqlServerIntegrationFact]
    public async Task ScenarioA_EnqueueGetsLockFirstAndResetCancelsItsOldGeneration()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var account = await CreateConfirmedUserAsync(database);
        var credentialsBefore = await ReadUserStateAsync(database, account.Id);
        var token = await GenerateTokenAsync(database, account.Id);

        await using var blockerContext = database.CreateVerificationContext();
        await using var blockerTransaction = await blockerContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await blockerContext.Database.OpenConnectionAsync();
        await using (var blockerCommand = blockerContext.Database.GetDbConnection().CreateCommand())
        {
            blockerCommand.Transaction = blockerTransaction.GetDbTransaction();
            blockerCommand.CommandText = """
                SELECT COUNT_BIG(*)
                FROM [dbo].[EmailOutbox] WITH (TABLOCKX, HOLDLOCK);
                """;
            await blockerCommand.ExecuteScalarAsync();
        }

        Task<SolicitarRecuperacaoSenhaResult>? enqueueTask = null;
        Task<PasswordResetResult>? resetTask = null;
        try
        {
            enqueueTask = Task.Run(() => RequestRecoveryAsync(database, account.Email));
            await WaitForApplicationLocksAsync(database, granted: 1, waiting: 0);
            Assert.False(enqueueTask.IsCompleted);

            resetTask = Task.Run(() =>
                ResetPasswordAsync(database, token.Email, token.Token, NewPassword));
            await WaitForApplicationLocksAsync(database, granted: 1, waiting: 1);
        }
        finally
        {
            await blockerTransaction.CommitAsync();
        }

        Assert.Equal(
            SolicitarRecuperacaoSenhaStatus.RequestProcessed,
            (await enqueueTask!.WaitAsync(TimeSpan.FromSeconds(30))).Status);
        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await resetTask!.WaitAsync(TimeSpan.FromSeconds(30))).Status);

        var message = await ReadSingleMessageAsync(database, account.Id);
        var credentialsAfter = await ReadUserStateAsync(database, account.Id);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, message.Status);
        Assert.Null(message.ActiveDeduplicationKey);
        Assert.Null(message.LeaseExpiresAtUtc);
        Assert.Equal(0, message.Attempts);
        Assert.True(FingerprintMatches(
            database,
            credentialsBefore.SecurityStamp,
            message.CredentialGenerationFingerprint));
        Assert.True(StampChanged(credentialsBefore.SecurityStamp, credentialsAfter.SecurityStamp));
        Assert.False(FingerprintMatches(
            database,
            credentialsAfter.SecurityStamp,
            message.CredentialGenerationFingerprint));
        Assert.False(await VerifyTokenAsync(database, account.Id, token.Token));
        Assert.True(await CheckPasswordAsync(database, account.Id, NewPassword));
        Assert.False(await CheckPasswordAsync(database, account.Id, OldPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioB_ResetGetsLockFirstAndEnqueueUsesTheCommittedGeneration()
    {
        var commitGate = new TransactionCommitGate();
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dbContextInterceptor: commitGate);
        var account = await CreateConfirmedUserAsync(database);
        var token = await GenerateTokenAsync(database, account.Id);
        var credentialsBefore = await ReadUserStateAsync(database, account.Id);
        commitGate.Arm();

        Task<PasswordResetResult>? resetTask = null;
        Task<PasswordResetOutboxResult>? enqueueTask = null;
        try
        {
            resetTask = Task.Run(() =>
                ResetPasswordAsync(database, token.Email, token.Token, NewPassword));
            await commitGate.CommitReached.Task.WaitAsync(TimeSpan.FromSeconds(30));

            enqueueTask = Task.Run(() => RequestOutboxAsync(database, account.Id));
            await WaitForApplicationLocksAsync(database, granted: 1, waiting: 1);
        }
        finally
        {
            commitGate.AllowCommit();
        }

        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await resetTask!.WaitAsync(TimeSpan.FromSeconds(30))).Status);
        Assert.Equal(
            PasswordResetOutboxResult.Enqueued,
            await enqueueTask!.WaitAsync(TimeSpan.FromSeconds(30)));

        var credentialsAfter = await ReadUserStateAsync(database, account.Id);
        var message = await ReadSingleMessageAsync(database, account.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.Equal($"password-reset:{account.Id:N}", message.ActiveDeduplicationKey);
        Assert.True(StampChanged(credentialsBefore.SecurityStamp, credentialsAfter.SecurityStamp));
        Assert.True(FingerprintMatches(
            database,
            credentialsAfter.SecurityStamp,
            message.CredentialGenerationFingerprint));
        Assert.False(FingerprintMatches(
            database,
            credentialsBefore.SecurityStamp,
            message.CredentialGenerationFingerprint));
        Assert.False(await VerifyTokenAsync(database, account.Id, token.Token));
        Assert.True(await CheckPasswordAsync(database, account.Id, NewPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioC_ClaimBeforeResetCannotSendOrApplyAnyStaleTransition()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var account = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, account.Email);
        var messageBeforeClaim = await ReadSingleMessageAsync(database, account.Id);

        await using var scope = CreateScope(database);
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IEmailOutboxStore>();
        var claim = Assert.Single(await store.ClaimDueAsync(
            EmailOutboxMessageType.PasswordReset,
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(2)));
        var processing = await ReadMessageAsync(database, claim.Id);
        var token = await GenerateTokenAsync(database, account.Id);
        Assert.True(FingerprintMatches(
            database,
            (await ReadUserStateAsync(database, account.Id)).SecurityStamp,
            claim.CredentialGenerationFingerprint?.ToArray()));

        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await services.GetRequiredService<IPasswordResetService>()
                .ResetPasswordAsync(token.Email, token.Token, NewPassword)).Status);

        var cancelled = await ReadMessageAsync(database, claim.Id);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ActiveDeduplicationKey);
        Assert.Null(cancelled.LeaseExpiresAtUtc);
        Assert.False(BytesEqual(processing.RowVersion, cancelled.RowVersion));
        Assert.False(await store.CompleteAsync(claim));
        Assert.False(await store.RetryAsync(claim, jitterSample: 0.5));
        Assert.False(await store.CancelAsync(claim));
        Assert.False(await store.DeadLetterAsync(claim));

        var fingerprint = claim.CredentialGenerationFingerprint?.ToArray();
        Assert.NotNull(fingerprint);
        try
        {
            var tokenResult = await services.GetRequiredService<IPasswordResetService>()
                .GeneratePasswordResetTokenForUserAsync(account.Id, fingerprint);
            Assert.Equal(PasswordResetTokenStatus.CredentialGenerationMismatch, tokenResult.Status);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fingerprint);
        }

        var sender = new RecordingEmailSender();
        await CreatePasswordResetProcessor(services, sender).ProcessAsync(claim);
        Assert.Equal(0, sender.SendCount);
        Assert.Empty(await store.ClaimDueAsync(
            EmailOutboxMessageType.PasswordReset,
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(2)));

        var afterStaleOperations = await ReadMessageAsync(database, claim.Id);
        AssertOutboxStateEqual(cancelled, afterStaleOperations);
        Assert.False(BytesEqual(messageBeforeClaim.RowVersion, cancelled.RowVersion));
        Assert.False(await VerifyTokenAsync(database, account.Id, token.Token));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioD_ResetDuringExternalSendCancelsTheClaimWithoutHoldingATransaction()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var account = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, account.Email);
        var credentialsBefore = await ReadUserStateAsync(database, account.Id);
        var pending = await ReadSingleMessageAsync(database, account.Id);

        using var handler = new PausedResendHandler();
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var workerScope = CreateScope(database);
        var workerServices = workerScope.ServiceProvider;
        var claim = Assert.Single(await workerServices.GetRequiredService<IEmailOutboxStore>()
            .ClaimDueAsync(
                EmailOutboxMessageType.PasswordReset,
                batchSize: 1,
                leaseDuration: TimeSpan.FromMinutes(2)));
        var processor = CreatePasswordResetProcessor(
            workerServices,
            CreateResendSender(httpClient));
        var processTask = Task.Run(() => processor.ProcessAsync(claim));

        try
        {
            await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var sentToken = handler.ReadResetToken();
            Assert.True(await VerifyTokenForSnapshotAsync(
                database,
                account.Id,
                account.Email,
                credentialsBefore.SecurityStamp,
                sentToken));

            var workerContext = workerServices.GetRequiredService<XaquiamuniDbContext>();
            Assert.Null(workerContext.Database.CurrentTransaction);
            await workerContext.Database.OpenConnectionAsync();
            try
            {
                var connection = Assert.IsType<SqlConnection>(workerContext.Database.GetDbConnection());
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT @@TRANCOUNT;";
                Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
            }
            finally
            {
                await workerContext.Database.CloseConnectionAsync();
            }

            Assert.Equal(
                PasswordResetStatus.Succeeded,
                (await ResetPasswordAsync(database, account.Email, sentToken, NewPassword))
                .Status);

            var credentialsAfter = await ReadUserStateAsync(database, account.Id);
            var cancelled = await ReadMessageAsync(database, pending.Id);
            Assert.True(StampChanged(credentialsBefore.SecurityStamp, credentialsAfter.SecurityStamp));
            Assert.False(await VerifyTokenForSnapshotAsync(
                database,
                account.Id,
                account.Email,
                credentialsAfter.SecurityStamp,
                sentToken));
            Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelled.Status);
            Assert.Null(cancelled.ActiveDeduplicationKey);
            Assert.Null(cancelled.LeaseExpiresAtUtc);
            Assert.False(BytesEqual(pending.RowVersion, cancelled.RowVersion));
        }
        finally
        {
            handler.AllowResponse();
        }

        await processTask.WaitAsync(TimeSpan.FromSeconds(30));
        var afterProcessorCompletion = await ReadMessageAsync(database, pending.Id);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, afterProcessorCompletion.Status);
        Assert.Null(afterProcessorCompletion.ActiveDeduplicationKey);
        Assert.Null(afterProcessorCompletion.LeaseExpiresAtUtc);
        Assert.False(BytesEqual(claim.RowVersion.ToArray(), afterProcessorCompletion.RowVersion));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioE_ResetAfterWorkerCompletionPreservesCompletedHistory()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var account = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, account.Email);
        var credentialsBefore = await ReadUserStateAsync(database, account.Id);

        using var handler = new PausedResendHandler(pauseResponse: false);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var scope = CreateScope(database);
        var services = scope.ServiceProvider;
        var claim = Assert.Single(await services.GetRequiredService<IEmailOutboxStore>()
            .ClaimDueAsync(
                EmailOutboxMessageType.PasswordReset,
                batchSize: 1,
                leaseDuration: TimeSpan.FromMinutes(2)));
        await CreatePasswordResetProcessor(services, CreateResendSender(httpClient))
            .ProcessAsync(claim);

        var completed = await ReadSingleMessageAsync(database, account.Id);
        Assert.Equal(EmailOutboxMessageStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Null(completed.ActiveDeduplicationKey);
        Assert.Null(completed.LeaseExpiresAtUtc);
        var sentToken = handler.ReadResetToken();
        Assert.True(await VerifyTokenAsync(database, account.Id, sentToken));

        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await ResetPasswordAsync(database, account.Email, sentToken, NewPassword)).Status);

        var after = await ReadSingleMessageAsync(database, account.Id);
        AssertOutboxStateEqual(completed, after);
        Assert.False(await VerifyTokenAsync(database, account.Id, sentToken));
        Assert.True(await CheckPasswordAsync(database, account.Id, NewPassword));
        Assert.False(await CheckPasswordAsync(database, account.Id, OldPassword));
        Assert.True(StampChanged(
            credentialsBefore.SecurityStamp,
            (await ReadUserStateAsync(database, account.Id)).SecurityStamp));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioF_RollbackReleasesLockAndEnqueueKeepsTheRestoredGeneration()
    {
        var commitGate = new TransactionCommitGate();
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dbContextInterceptor: commitGate);
        var account = await CreateConfirmedUserAsync(database);
        await RequestRecoveryAsync(database, account.Email);
        var messageBefore = await ReadSingleMessageAsync(database, account.Id);
        var credentialsBefore = await ReadUserStateAsync(database, account.Id);
        var token = await GenerateTokenAsync(database, account.Id);

        await using var resetScope = CreateScope(database);
        var resetServices = resetScope.ServiceProvider;
        var dbContext = resetServices.GetRequiredService<XaquiamuniDbContext>();
        var trackedMessage = await dbContext.EmailOutboxMessages
            .SingleAsync(message => message.Id == messageBefore.Id);
        commitGate.Arm(failCommit: true, cancelledMessageId: messageBefore.Id);

        Task<PasswordResetResult>? resetTask = null;
        Task<PasswordResetOutboxResult>? enqueueTask = null;
        try
        {
            resetTask = Task.Run(() => resetServices
                .GetRequiredService<IPasswordResetService>()
                .ResetPasswordAsync(token.Email, token.Token, NewPassword));
            await commitGate.CommitReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(commitGate.CancellationVisibleBeforeCommit);

            enqueueTask = Task.Run(() => RequestOutboxAsync(database, account.Id));
            await WaitForApplicationLocksAsync(database, granted: 1, waiting: 1);
        }
        finally
        {
            commitGate.AllowCommit();
        }

        await Assert.ThrowsAsync<InjectedCommitFailureException>(async () =>
            await resetTask!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(
            PasswordResetOutboxResult.AlreadyQueued,
            await enqueueTask!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(EntityState.Detached, dbContext.Entry(trackedMessage).State);
        await dbContext.SaveChangesAsync();

        var credentialsAfter = await ReadUserStateAsync(database, account.Id);
        var messageAfter = await ReadMessageAsync(database, messageBefore.Id);
        AssertCredentialStateEqual(credentialsBefore, credentialsAfter);
        AssertOutboxStateEqual(messageBefore, messageAfter);
        Assert.Single(await ReadMessagesAsync(database, account.Id));
        Assert.True(await VerifyTokenAsync(database, account.Id, token.Token));
        Assert.True(await CheckPasswordAsync(database, account.Id, OldPassword));
        Assert.False(await CheckPasswordAsync(database, account.Id, NewPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioG_ResetLockForOneUserDoesNotSerializeRecoveryForAnother()
    {
        var commitGate = new TransactionCommitGate();
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dbContextInterceptor: commitGate);
        var accountA = await CreateConfirmedUserAsync(database);
        var accountB = await CreateConfirmedUserAsync(database);
        var credentialsA = await ReadUserStateAsync(database, accountA.Id);
        var credentialsB = await ReadUserStateAsync(database, accountB.Id);
        var tokenA = await GenerateTokenAsync(database, accountA.Id);
        commitGate.Arm();

        Task<PasswordResetResult>? resetTask = null;
        Task<PasswordResetOutboxResult>? recoveryTask = null;
        try
        {
            resetTask = Task.Run(() =>
                ResetPasswordAsync(database, tokenA.Email, tokenA.Token, NewPassword));
            await commitGate.CommitReached.Task.WaitAsync(TimeSpan.FromSeconds(30));

            recoveryTask = Task.Run(() => RequestOutboxAsync(database, accountB.Id));
            Assert.Equal(
                PasswordResetOutboxResult.Enqueued,
                await recoveryTask.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.False(commitGate.IsReleased);
            Assert.False(resetTask.IsCompleted);
        }
        finally
        {
            commitGate.AllowCommit();
        }

        Assert.Equal(PasswordResetStatus.Succeeded, (await resetTask!.WaitAsync(TimeSpan.FromSeconds(30))).Status);
        var credentialsBAfter = await ReadUserStateAsync(database, accountB.Id);
        var messageB = await ReadSingleMessageAsync(database, accountB.Id);
        AssertCredentialStateEqual(credentialsB, credentialsBAfter);
        Assert.Equal(EmailOutboxMessageStatus.Pending, messageB.Status);
        Assert.Equal($"password-reset:{accountB.Id:N}", messageB.ActiveDeduplicationKey);
        Assert.True(FingerprintMatches(
            database,
            credentialsBAfter.SecurityStamp,
            messageB.CredentialGenerationFingerprint));
        Assert.True(StampChanged(
            credentialsA.SecurityStamp,
            (await ReadUserStateAsync(database, accountA.Id)).SecurityStamp));
        Assert.False(await VerifyTokenAsync(database, accountA.Id, tokenA.Token));
        Assert.False(await VerifyTokenAsync(database, accountB.Id, tokenA.Token));
        Assert.True(await CheckPasswordAsync(database, accountB.Id, OldPassword));
    }

    private static async Task<TestAccount> CreateConfirmedUserAsync(SqlServerTestDatabase database)
    {
        await using var scope = CreateScope(database);
        var email = $"password-reset-concurrency-{Guid.NewGuid():N}@example.test";
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
        return new TestAccount(user.Id, email);
    }

    private static async Task<SolicitarRecuperacaoSenhaResult> RequestRecoveryAsync(
        SqlServerTestDatabase database,
        string email)
    {
        await using var scope = CreateScope(database);
        return await scope.ServiceProvider
            .GetRequiredService<ISolicitarRecuperacaoSenha>()
            .ExecuteAsync(email);
    }

    private static async Task<PasswordResetOutboxResult> RequestOutboxAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var scope = CreateScope(database);
        return await scope.ServiceProvider
            .GetRequiredService<IPasswordResetOutbox>()
            .RequestAsync(userId);
    }

    private static async Task<GeneratedResetToken> GenerateTokenAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var scope = CreateScope(database);
        var services = scope.ServiceProvider;
        var userManager = services.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var fingerprint = services.GetRequiredService<ICredentialGenerationFingerprint>()
            .Compute(user.SecurityStamp!);
        try
        {
            var result = await services.GetRequiredService<IPasswordResetService>()
                .GeneratePasswordResetTokenForUserAsync(userId, fingerprint);
            Assert.Equal(PasswordResetTokenStatus.Generated, result.Status);
            Assert.NotNull(result.Email);
            Assert.NotNull(result.Token);
            return new GeneratedResetToken(result.Email!, result.Token!);
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

    private static async Task<UserCredentialState> ReadUserStateAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        var user = await context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
        return new UserCredentialState(
            user.PasswordHash!,
            user.SecurityStamp!,
            user.ConcurrencyStamp!,
            user.EmailConfirmed);
    }

    private static async Task<OutboxState> ReadSingleMessageAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        return ToOutboxState(await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message =>
                message.UserId == userId
                && message.MessageType == EmailOutboxMessageType.PasswordReset));
    }

    private static async Task<OutboxState> ReadMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        return ToOutboxState(await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Id == messageId));
    }

    private static async Task<List<OutboxState>> ReadMessagesAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        return (await context.EmailOutboxMessages.AsNoTracking()
                .Where(message => message.UserId == userId)
                .ToListAsync())
            .Select(ToOutboxState)
            .ToList();
    }

    private static OutboxState ToOutboxState(EmailOutboxMessage message) =>
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

    private static bool SameOutboxState(OutboxState first, OutboxState second) =>
        first.Id == second.Id
        && first.UserId == second.UserId
        && first.MessageType == second.MessageType
        && first.Status == second.Status
        && first.Attempts == second.Attempts
        && first.CreatedAtUtc == second.CreatedAtUtc
        && first.NextAttemptAtUtc == second.NextAttemptAtUtc
        && first.CompletedAtUtc == second.CompletedAtUtc
        && string.Equals(
            first.ActiveDeduplicationKey,
            second.ActiveDeduplicationKey,
            StringComparison.Ordinal)
        && first.LeaseExpiresAtUtc == second.LeaseExpiresAtUtc
        && NullableBytesEqual(first.CredentialGenerationFingerprint, second.CredentialGenerationFingerprint)
        && BytesEqual(first.RowVersion, second.RowVersion);

    private static void AssertOutboxStateEqual(OutboxState expected, OutboxState actual) =>
        Assert.True(SameOutboxState(expected, actual), "The Outbox history changed unexpectedly.");

    private static bool NullableBytesEqual(byte[]? first, byte[]? second) =>
        first is null
            ? second is null
            : second is not null && BytesEqual(first, second);

    private static bool BytesEqual(byte[]? first, byte[]? second) =>
        first is not null
        && second is not null
        && first.Length == second.Length
        && CryptographicOperations.FixedTimeEquals(first, second);

    private static bool FingerprintMatches(
        SqlServerTestDatabase database,
        string securityStamp,
        byte[]? fingerprint)
    {
        if (fingerprint is null)
        {
            return false;
        }

        var expected = database.Services
            .GetRequiredService<ICredentialGenerationFingerprint>()
            .Compute(securityStamp);
        try
        {
            return BytesEqual(expected, fingerprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static bool StampChanged(string first, string second) =>
        !string.Equals(first, second, StringComparison.Ordinal);

    private static bool SameCredentialState(UserCredentialState first, UserCredentialState second) =>
        string.Equals(first.PasswordHash, second.PasswordHash, StringComparison.Ordinal)
        && string.Equals(first.SecurityStamp, second.SecurityStamp, StringComparison.Ordinal)
        && string.Equals(first.ConcurrencyStamp, second.ConcurrencyStamp, StringComparison.Ordinal)
        && first.EmailConfirmed == second.EmailConfirmed;

    private static void AssertCredentialStateEqual(
        UserCredentialState expected,
        UserCredentialState actual) =>
        Assert.True(SameCredentialState(expected, actual), "The user credential state changed unexpectedly.");

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
        return await VerifyTokenAsync(userManager, user, token);
    }

    private static async Task<bool> VerifyTokenForSnapshotAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string email,
        string securityStamp,
        string token)
    {
        await using var scope = CreateScope(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var snapshot = new XaquiamuniUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            SecurityStamp = securityStamp
        };
        return await VerifyTokenAsync(userManager, snapshot, token);
    }

    private static Task<bool> VerifyTokenAsync(
        UserManager<XaquiamuniUser> userManager,
        XaquiamuniUser user,
        string token) =>
        userManager.VerifyUserTokenAsync(
            user,
            XaquiamuniPasswordResetTokenProvider.ProviderName,
            PasswordResetPurpose,
            token);

    private static async Task WaitForApplicationLocksAsync(
        SqlServerTestDatabase database,
        int granted,
        int waiting)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await using var context = database.CreateVerificationContext();
            await context.Database.OpenConnectionAsync();
            try
            {
                var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT
                        SUM(CASE WHEN [request_status] = N'GRANT' THEN 1 ELSE 0 END),
                        SUM(CASE WHEN [request_status] = N'WAIT' THEN 1 ELSE 0 END)
                    FROM sys.dm_tran_locks
                    WHERE [resource_type] = N'APPLICATION'
                        AND [resource_database_id] = DB_ID()
                        AND [request_mode] = N'X'
                        AND [request_owner_type] = N'TRANSACTION';
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var grantedCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    var waitingCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    if (grantedCount >= granted && waitingCount >= waiting)
                    {
                        return;
                    }
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }

            await Task.Yield();
        }

        throw new Xunit.Sdk.XunitException(
            "The expected per-user application-lock coordination was not observed.");
    }

    private static AsyncServiceScope CreateScope(SqlServerTestDatabase database) =>
        database.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

    private static PasswordResetOutboxProcessor CreatePasswordResetProcessor(
        IServiceProvider services,
        IEmailSender emailSender) =>
        new(
            services.GetRequiredService<IEmailOutboxStore>(),
            services.GetRequiredService<IPasswordResetService>(),
            new PasswordResetMessageComposer(
                Options.Create(new EmailConfirmationEmailOptions
                {
                    PublicFrontendBaseUrl = "https://frontend.example.test"
                }),
                new TestHostEnvironment()),
            emailSender,
            services.GetRequiredService<TimeProvider>());

    private static ResendEmailSender CreateResendSender(HttpClient httpClient) =>
        new(
            httpClient,
            Options.Create(new ResendEmailOptions
            {
                ApiKey = SyntheticApiKey,
                FromEmail = "no-reply@example.test"
            }));

    private sealed class RecordingEmailSender : IEmailSender
    {
        private int _sendCount;

        public int SendCount => Volatile.Read(ref _sendCount);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sendCount);
            return Task.CompletedTask;
        }
    }

    private sealed class PausedResendHandler(bool pauseResponse = true) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _requestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _requestBody;

        public TaskCompletionSource<bool> RequestStarted => _requestStarted;

        public void AllowResponse() => _allowResponse.TrySetResult(true);

        public string ReadResetToken()
        {
            var requestBody = _requestBody
                ?? throw new Xunit.Sdk.XunitException("The simulated mail request was not captured.");
            using var document = JsonDocument.Parse(requestBody);
            var html = WebUtility.HtmlDecode(document.RootElement.GetProperty("html").GetString()!);
            var hrefStart = html.IndexOf("href=\"", StringComparison.Ordinal);
            if (hrefStart < 0)
            {
                throw new Xunit.Sdk.XunitException("The simulated mail did not contain a reset link.");
            }

            hrefStart += "href=\"".Length;
            var hrefEnd = html.IndexOf('"', hrefStart);
            if (hrefEnd <= hrefStart
                || !Uri.TryCreate(html[hrefStart..hrefEnd], UriKind.Absolute, out var link))
            {
                throw new Xunit.Sdk.XunitException("The simulated reset link was invalid.");
            }

            var tokenPart = link.Fragment
                .TrimStart('#')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .SingleOrDefault(parts =>
                    parts.Length == 2
                    && string.Equals(
                        Uri.UnescapeDataString(parts[0]),
                        "token",
                        StringComparison.Ordinal));
            if (tokenPart is null)
            {
                throw new Xunit.Sdk.XunitException("The simulated reset link did not contain a token.");
            }

            return Uri.UnescapeDataString(tokenPart[1]);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            _requestStarted.TrySetResult(true);
            if (pauseResponse)
            {
                await _allowResponse.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class TransactionCommitGate : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> _commitReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid? _cancelledMessageId;
        private int _armed;
        private bool _failCommit;

        public TaskCompletionSource<bool> CommitReached => _commitReached;
        public bool CancellationVisibleBeforeCommit { get; private set; }
        public bool IsReleased => _allowCommit.Task.IsCompleted;

        public void Arm(bool failCommit = false, Guid? cancelledMessageId = null)
        {
            _failCommit = failCommit;
            _cancelledMessageId = cancelledMessageId;
            Interlocked.Exchange(ref _armed, 1);
        }

        public void AllowCommit() => _allowCommit.TrySetResult(true);

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

            if (_cancelledMessageId is Guid messageId)
            {
                var connection = Assert.IsType<SqlConnection>(transaction.Connection);
                await using var command = connection.CreateCommand();
                command.Transaction = Assert.IsType<SqlTransaction>(transaction);
                command.CommandText = "SELECT [Status] FROM [dbo].[EmailOutbox] WHERE [Id] = @Id;";
                command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier)
                {
                    Value = messageId
                });
                var status = await command.ExecuteScalarAsync(cancellationToken);
                CancellationVisibleBeforeCommit =
                    status is not null
                    && status is not DBNull
                    && Convert.ToInt16(status) == (short)EmailOutboxMessageStatus.Cancelled;
            }

            _commitReached.TrySetResult(true);
            await _allowCommit.Task.WaitAsync(cancellationToken);
            if (_failCommit)
            {
                throw new InjectedCommitFailureException();
            }

            return result;
        }
    }

    private sealed class InjectedCommitFailureException : Exception
    {
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Xaquiamuni.Tests";
        public string ContentRootPath { get; set; } = Environment.CurrentDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record TestAccount(Guid Id, string Email);
    private sealed record GeneratedResetToken(string Email, string Token);
    private sealed record UserCredentialState(
        string PasswordHash,
        string SecurityStamp,
        string ConcurrencyStamp,
        bool EmailConfirmed);
    private sealed record OutboxState(
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
}
