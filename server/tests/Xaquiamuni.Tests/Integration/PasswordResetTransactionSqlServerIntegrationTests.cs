using System.Data;
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;
using Xaquiamuni.Infraestrutura.Seguranca;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class PasswordResetTransactionSqlServerIntegrationTests
{
    private const string OldPassword = "Old Password 987!";
    private const string NewPassword = "New Password 987!";
    private const string OtherNewPassword = "Other Password 987!";
    private const string PasswordResetPurpose = "ResetPassword";

    [SqlServerIntegrationFact]
    public async Task ScenarioA_ValidResetCommitsIdentityChangesAndConsumesToken()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateConfirmedUserAsync(database);
        var token = await GenerateTokenAsync(database, userId);
        var before = await ReadUserAsync(database, userId);

        var result = await ResetPasswordAsync(database, token.Email, token.Token, NewPassword);
        var after = await ReadUserAsync(database, userId);

        Assert.Equal(PasswordResetStatus.Succeeded, result.Status);
        Assert.True(await CheckPasswordAsync(database, userId, NewPassword));
        Assert.False(await CheckPasswordAsync(database, userId, OldPassword));
        Assert.False(string.Equals(before.SecurityStamp, after.SecurityStamp, StringComparison.Ordinal));
        Assert.False(string.Equals(
            before.ConcurrencyStamp,
            after.ConcurrencyStamp,
            StringComparison.Ordinal));
        Assert.True(after.EmailConfirmed);
        Assert.False(await VerifyTokenAsync(database, userId, token.Token));

        var reuse = await ResetPasswordAsync(
            database,
            token.Email,
            token.Token,
            OtherNewPassword);
        Assert.Equal(PasswordResetStatus.InvalidRequest, reuse.Status);
        Assert.True(await CheckPasswordAsync(database, userId, NewPassword));
        Assert.False(await CheckPasswordAsync(database, userId, OtherNewPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioB_InvalidTokenDoesNotCommitIdentityChanges()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var email = $"password-reset-{Guid.NewGuid():N}@example.test";
        var userId = await CreateConfirmedUserAsync(database, email);
        var before = await ReadUserAsync(database, userId);

        var result = await ResetPasswordAsync(
            database,
            email,
            "invalid-password-reset-token",
            NewPassword);
        var after = await ReadUserAsync(database, userId);

        Assert.Equal(PasswordResetStatus.InvalidRequest, result.Status);
        Assert.True(SameCredentialState(before, after));
        Assert.True(await CheckPasswordAsync(database, userId, OldPassword));
        Assert.False(await CheckPasswordAsync(database, userId, NewPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioC_InvalidPasswordPreservesStateAndTokenForLaterAttempt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateConfirmedUserAsync(database);
        var token = await GenerateTokenAsync(database, userId);
        var before = await ReadUserAsync(database, userId);

        var invalidResult = await ResetPasswordAsync(database, token.Email, token.Token, "weak");
        var afterInvalid = await ReadUserAsync(database, userId);

        Assert.Equal(PasswordResetStatus.InvalidPassword, invalidResult.Status);
        Assert.True(SameCredentialState(before, afterInvalid));
        Assert.True(await CheckPasswordAsync(database, userId, OldPassword));
        Assert.True(await VerifyTokenAsync(database, userId, token.Token));

        var validResult = await ResetPasswordAsync(database, token.Email, token.Token, NewPassword);
        Assert.Equal(PasswordResetStatus.Succeeded, validResult.Status);
        Assert.True(await CheckPasswordAsync(database, userId, NewPassword));
        Assert.False(await CheckPasswordAsync(database, userId, OldPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioD_ConcurrentResetsWithSameTokenOnlyAllowOneWinner()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateConfirmedUserAsync(database);
        var token = await GenerateTokenAsync(database, userId);

        await using var lockContext = database.CreateVerificationContext();
        await using var lockTransaction = await lockContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var connection = Assert.IsType<SqlConnection>(lockContext.Database.GetDbConnection());
        var sqlTransaction = Assert.IsType<SqlTransaction>(lockTransaction.GetDbTransaction());
        await PasswordResetGenerationLock.AcquireAsync(
            connection,
            sqlTransaction,
            userId,
            CancellationToken.None);

        await using var firstScope = CreateScope(database);
        await using var secondScope = CreateScope(database);
        var firstService = firstScope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ready = new CountdownEvent(2);

        var firstTask = Task.Run(async () =>
        {
            ready.Signal();
            await start.Task.ConfigureAwait(false);
            return await firstService.ResetPasswordAsync(token.Email, token.Token, NewPassword)
                .ConfigureAwait(false);
        });
        var secondTask = Task.Run(async () =>
        {
            ready.Signal();
            await start.Task.ConfigureAwait(false);
            return await secondService.ResetPasswordAsync(token.Email, token.Token, OtherNewPassword)
                .ConfigureAwait(false);
        });

        Assert.True(ready.Wait(TimeSpan.FromSeconds(30)));
        start.SetResult(true);
        await WaitForApplicationLockWaitersAsync(database, expectedWaiters: 2);
        await lockTransaction.CommitAsync();

        var results = await Task.WhenAll(firstTask, secondTask);
        var winner = Assert.Single(results, result => result.Status == PasswordResetStatus.Succeeded);
        var rejected = Assert.Single(results, result => result.Status == PasswordResetStatus.InvalidRequest);
        Assert.True(winner.Succeeded);
        Assert.False(rejected.Succeeded);

        var firstPasswordValid = await CheckPasswordAsync(database, userId, NewPassword);
        var secondPasswordValid = await CheckPasswordAsync(database, userId, OtherNewPassword);
        Assert.NotEqual(firstPasswordValid, secondPasswordValid);
        Assert.False(await CheckPasswordAsync(database, userId, OldPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioG_TrackedUserIsReloadedAfterLockBeforeTokenValidation()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateConfirmedUserAsync(database);
        var token = await GenerateTokenAsync(database, userId);

        await using var resetScope = CreateScope(database);
        var userManager = resetScope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = resetScope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var trackedUser = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(trackedUser);
        var trackedSecurityStampBefore = trackedUser.SecurityStamp;

        await UpdateSecurityStampAsync(database, userId);
        var persistedBeforeReset = await ReadUserAsync(database, userId);
        Assert.False(string.Equals(
            trackedSecurityStampBefore,
            persistedBeforeReset.SecurityStamp,
            StringComparison.Ordinal));

        var result = await service.ResetPasswordAsync(token.Email, token.Token, NewPassword);

        Assert.Equal(PasswordResetStatus.InvalidRequest, result.Status);
        Assert.True(SameValue(
            persistedBeforeReset.SecurityStamp,
            trackedUser.SecurityStamp), "The tracked user was not reloaded after the lock.");
        var persistedAfterReset = await ReadUserAsync(database, userId);
        Assert.True(SameCredentialState(persistedBeforeReset, persistedAfterReset));
        Assert.True(await CheckPasswordAsync(database, userId, OldPassword));
        Assert.False(await CheckPasswordAsync(database, userId, NewPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioE_LockAcquisitionFailureDoesNotResetPasswordAndReleasesResources()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateConfirmedUserAsync(database);
        var token = await GenerateTokenAsync(database, userId);
        var before = await ReadUserAsync(database, userId);

        await using (var lockContext = database.CreateVerificationContext())
        await using (var lockTransaction = await lockContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            var connection = Assert.IsType<SqlConnection>(lockContext.Database.GetDbConnection());
            var transaction = Assert.IsType<SqlTransaction>(lockTransaction.GetDbTransaction());
            await PasswordResetGenerationLock.AcquireAsync(
                connection,
                transaction,
                userId,
                CancellationToken.None);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ResetPasswordAsync(database, token.Email, token.Token, NewPassword));
            Assert.Contains("could not be acquired", exception.Message, StringComparison.Ordinal);
        }

        var afterLockFailure = await ReadUserAsync(database, userId);
        Assert.True(SameCredentialState(before, afterLockFailure));
        Assert.True(await CheckPasswordAsync(database, userId, OldPassword));

        var retry = await ResetPasswordAsync(database, token.Email, token.Token, NewPassword);
        Assert.Equal(PasswordResetStatus.Succeeded, retry.Status);
        Assert.True(await CheckPasswordAsync(database, userId, NewPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioF_CancellationAfterIdentitySaveRollsBackBeforeCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var saveInterceptor = new CancelAfterSuccessfulSaveInterceptor(cancellation);
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dbContextInterceptor: saveInterceptor);
        var userId = await CreateConfirmedUserAsync(database);
        var token = await GenerateTokenAsync(database, userId);
        var before = await ReadUserAsync(database, userId);
        saveInterceptor.Arm();

        await using var scope = CreateScope(database);
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ResetPasswordAsync(
                token.Email,
                token.Token,
                NewPassword,
                cancellation.Token));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
        var trackedUser = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(trackedUser);
        Assert.True(SameCredentialState(before, ToCredentialState(trackedUser)));
        await dbContext.SaveChangesAsync();

        var afterRollback = await ReadUserAsync(database, userId);
        Assert.True(SameCredentialState(before, afterRollback));
        Assert.True(await CheckPasswordAsync(database, userId, OldPassword));
        Assert.False(await CheckPasswordAsync(database, userId, NewPassword));
        Assert.True(await VerifyTokenAsync(database, userId, token.Token));
    }

    private static async Task<Guid> CreateConfirmedUserAsync(
        SqlServerTestDatabase database,
        string? email = null)
    {
        await using var scope = CreateScope(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var userEmail = email ?? $"password-reset-{Guid.NewGuid():N}@example.test";
        var user = new XaquiamuniUser
        {
            UserName = userEmail,
            Email = userEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, OldPassword);
        Assert.True(result.Succeeded);
        return user.Id;
    }

    private static async Task<GeneratedResetToken> GenerateTokenAsync(
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
        var generated = await scope.ServiceProvider
            .GetRequiredService<IPasswordResetService>()
            .GeneratePasswordResetTokenForUserAsync(userId, fingerprint);

        Assert.Equal(PasswordResetTokenStatus.Generated, generated.Status);
        Assert.NotNull(generated.Email);
        Assert.NotNull(generated.Token);
        return new GeneratedResetToken(generated.Email!, generated.Token!);
    }

    private static async Task<PasswordResetResult> ResetPasswordAsync(
        SqlServerTestDatabase database,
        string email,
        string token,
        string newPassword)
    {
        await using var scope = CreateScope(database);
        return await scope.ServiceProvider
            .GetRequiredService<IPasswordResetService>()
            .ResetPasswordAsync(email, token, newPassword);
    }

    private static async Task<UserCredentialState> ReadUserAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == userId);
        return new UserCredentialState(
            user.PasswordHash!,
            user.SecurityStamp!,
            user.ConcurrencyStamp!,
            user.EmailConfirmed);
    }

    private static UserCredentialState ToCredentialState(XaquiamuniUser user) =>
        new(
            user.PasswordHash!,
            user.SecurityStamp!,
            user.ConcurrencyStamp!,
            user.EmailConfirmed);

    private static async Task UpdateSecurityStampAsync(
        SqlServerTestDatabase database,
        Guid userId)
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
            PasswordResetPurpose,
            token);
    }

    private static bool SameCredentialState(UserCredentialState first, UserCredentialState second) =>
        SameValue(first.PasswordHash, second.PasswordHash)
        && SameValue(first.SecurityStamp, second.SecurityStamp)
        && SameValue(first.ConcurrencyStamp, second.ConcurrencyStamp)
        && first.EmailConfirmed == second.EmailConfirmed;

    private static bool SameValue(string? first, string? second) =>
        string.Equals(first, second, StringComparison.Ordinal);

    private static AsyncServiceScope CreateScope(SqlServerTestDatabase database) =>
        database.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

    private static async Task WaitForApplicationLockWaitersAsync(
        SqlServerTestDatabase database,
        int expectedWaiters)
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
                    SELECT COUNT(*)
                    FROM sys.dm_tran_locks
                    WHERE [resource_type] = N'APPLICATION'
                        AND [resource_database_id] = DB_ID()
                        AND [request_status] = N'WAIT'
                        AND [request_mode] = N'X';
                    """;
                var waiters = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (waiters >= expectedWaiters)
                {
                    return;
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }

            await Task.Yield();
        }

        throw new Xunit.Sdk.XunitException(
            "Concurrent password resets did not both wait for the user application lock.");
    }

    private sealed record GeneratedResetToken(string Email, string Token);

    private sealed record UserCredentialState(
        string PasswordHash,
        string SecurityStamp,
        string ConcurrencyStamp,
        bool EmailConfirmed);

    private sealed class CancelAfterSuccessfulSaveInterceptor(
        CancellationTokenSource cancellationTokenSource) : SaveChangesInterceptor
    {
        private int _armed;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                cancellationTokenSource.Cancel();
            }

            return ValueTask.FromResult(result);
        }
    }
}
