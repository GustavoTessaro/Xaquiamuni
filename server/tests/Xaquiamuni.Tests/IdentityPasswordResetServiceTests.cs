using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;
using Xaquiamuni.Infraestrutura.Seguranca;

namespace Xaquiamuni.Tests;

public sealed class IdentityPasswordResetServiceTests
{
    private const string OldPassword = "Old Password 987!";
    private const string NewPassword = "New Password 987!";

    [Fact]
    public async Task PasswordResetService_IsRegisteredAsScoped()
    {
        await using var provider = CreateProvider();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var firstService = firstScope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IPasswordResetService>();

        Assert.NotSame(firstService, secondService);
    }

    [Fact]
    public async Task FindConfirmedUserIdByEmailAsync_ReturnsOnlyConfirmedUsersWithoutMutatingThem()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var confirmedUser = await CreateUserAsync(userManager, "Confirmed@example.test", confirmed: true);
        var unconfirmedUser = await CreateUserAsync(userManager, "Unconfirmed@example.test", confirmed: false);
        var confirmedStamp = confirmedUser.SecurityStamp;
        var confirmedPasswordHash = confirmedUser.PasswordHash;
        var unconfirmedStamp = unconfirmedUser.SecurityStamp;

        var eligible = await service.FindConfirmedUserIdByEmailAsync("  CONFIRMED@example.test ");
        var unconfirmed = await service.FindConfirmedUserIdByEmailAsync("Unconfirmed@example.test");
        var missing = await service.FindConfirmedUserIdByEmailAsync("missing@example.test");
        var invalid = await service.FindConfirmedUserIdByEmailAsync("not-an-email");

        Assert.Equal(confirmedUser.Id, eligible);
        Assert.Null(unconfirmed);
        Assert.Null(missing);
        Assert.Null(invalid);
        Assert.Equal(confirmedStamp, confirmedUser.SecurityStamp);
        Assert.Equal(confirmedPasswordHash, confirmedUser.PasswordHash);
        Assert.Equal(unconfirmedStamp, unconfirmedUser.SecurityStamp);
        Assert.True(confirmedUser.EmailConfirmed);
        Assert.False(unconfirmedUser.EmailConfirmed);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenForUserAsync_OnlyReturnsTokenForEligibleUser()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var confirmedUser = await CreateUserAsync(userManager, "current@example.test", confirmed: true);
        var unconfirmedUser = await CreateUserAsync(userManager, "unconfirmed@example.test", confirmed: false);

        var generated = await service.GeneratePasswordResetTokenForUserAsync(
            confirmedUser.Id,
            ComputeFingerprint(confirmedUser));
        var unconfirmed = await service.GeneratePasswordResetTokenForUserAsync(
            unconfirmedUser.Id,
            ComputeFingerprint(unconfirmedUser));
        var missing = await service.GeneratePasswordResetTokenForUserAsync(
            Guid.NewGuid(),
            ComputeFingerprint(confirmedUser));

        Assert.Equal(PasswordResetTokenStatus.Generated, generated.Status);
        Assert.Equal(confirmedUser.Email, generated.Email);
        Assert.False(string.IsNullOrWhiteSpace(generated.Token));
        Assert.True(await userManager.VerifyUserTokenAsync(
            confirmedUser,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            generated.Token!));
        Assert.Equal(PasswordResetTokenStatus.NotEligible, unconfirmed.Status);
        Assert.Null(unconfirmed.Email);
        Assert.Null(unconfirmed.Token);
        Assert.Equal(PasswordResetTokenStatus.NotEligible, missing.Status);
        Assert.DoesNotContain(confirmedUser.Email!, generated.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(generated.Token!, generated.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenForUserAsync_RejectsMissingOrInvalidFingerprint()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "fingerprint-input@example.test", confirmed: true);

        var missing = await service.GeneratePasswordResetTokenForUserAsync(user.Id, null);
        var shortFingerprint = await service.GeneratePasswordResetTokenForUserAsync(user.Id, new byte[31]);
        var longFingerprint = await service.GeneratePasswordResetTokenForUserAsync(user.Id, new byte[33]);

        Assert.Equal(PasswordResetTokenStatus.InvalidRequest, missing.Status);
        Assert.Equal(PasswordResetTokenStatus.InvalidRequest, shortFingerprint.Status);
        Assert.Equal(PasswordResetTokenStatus.InvalidRequest, longFingerprint.Status);
        Assert.Null(missing.Token);
        Assert.Null(shortFingerprint.Token);
        Assert.Null(longFingerprint.Token);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenForUserAsync_RejectsOldGenerationAndAcceptsCurrentFingerprint()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var store = scope.ServiceProvider.GetRequiredService<InMemoryUserStore>();
        var user = await CreateUserAsync(userManager, "changed-stamp@example.test", confirmed: true);
        var oldFingerprint = ComputeFingerprint(user);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var newFingerprint = ComputeFingerprint(user);
        var userIdCallsBeforeOldGeneration = store.GetUserIdCalls;

        var oldGeneration = await service.GeneratePasswordResetTokenForUserAsync(user.Id, oldFingerprint);
        var userIdCallsAfterOldGeneration = store.GetUserIdCalls;
        var currentGeneration = await service.GeneratePasswordResetTokenForUserAsync(user.Id, newFingerprint);
        var currentTokenIsValid = await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            currentGeneration.Token!);

        Assert.Equal(PasswordResetTokenStatus.CredentialGenerationMismatch, oldGeneration.Status);
        Assert.Equal(userIdCallsBeforeOldGeneration, userIdCallsAfterOldGeneration);
        Assert.Null(oldGeneration.Email);
        Assert.Null(oldGeneration.Token);
        Assert.Equal(PasswordResetTokenStatus.Generated, currentGeneration.Status);
        Assert.True(currentTokenIsValid);
        Assert.DoesNotContain(user.Email!, oldGeneration.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(user.SecurityStamp!, oldGeneration.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(currentGeneration.Token!, currentGeneration.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-email")]
    public async Task GeneratePasswordResetTokenForUserAsync_RejectsUserWithoutValidEmail(string? email)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "valid-email@example.test", confirmed: true);
        user.Email = email;

        var result = await service.GeneratePasswordResetTokenForUserAsync(
            user.Id,
            ComputeFingerprint(user));

        Assert.Equal(PasswordResetTokenStatus.NotEligible, result.Status);
        Assert.Null(result.Token);
        Assert.Null(result.Email);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenForUserAsync_UsesValidatedSnapshotWhenStampChangesBeforeProviderReadsIt()
    {
        var store = new InMemoryUserStore(returnSnapshotOnFindById: true);
        await using var provider = CreateProvider(userStore: store);
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "snapshot@example.test", confirmed: true);
        var stampA = user.SecurityStamp!;
        var stampB = Guid.NewGuid().ToString("N");
        var fingerprintA = ComputeFingerprint(user);
        store.BeforeGetUserId = _ => store.SetPersistedSecurityStamp(user.Id, stampB);

        var result = await service.GeneratePasswordResetTokenForUserAsync(user.Id, fingerprintA);
        var snapshotA = store.LastUserById!;
        var stampUsedByTokenProvider = store.LastSecurityStampRead;
        var persistedUser = await store.FindByIdAsync(user.Id.ToString(), CancellationToken.None);
        var tokenIsValidForSnapshotA = await userManager.VerifyUserTokenAsync(
            snapshotA,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            result.Token!);
        var tokenIsValidForGenerationB = await userManager.VerifyUserTokenAsync(
            persistedUser!,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            result.Token!);

        Assert.Equal(PasswordResetTokenStatus.Generated, result.Status);
        Assert.Equal(stampA, stampUsedByTokenProvider);
        Assert.Equal(stampA, snapshotA.SecurityStamp);
        Assert.Equal(stampB, persistedUser!.SecurityStamp);
        Assert.True(tokenIsValidForSnapshotA);
        Assert.False(tokenIsValidForGenerationB);
    }

    [Fact]
    public async Task EntityFrameworkUserStore_ReadsSecurityStampFromPassedUserSnapshotWithoutOpeningConnection()
    {
        const string snapshotSecurityStamp = "snapshot-generation";
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniPasswordResetSnapshotTest")
            .Options;
        using var dbContext = new XaquiamuniDbContext(options);
        using var store = new UserStore<
            XaquiamuniUser,
            IdentityRole<Guid>,
            XaquiamuniDbContext,
            Guid>(dbContext);
        var userSnapshot = new XaquiamuniUser { SecurityStamp = snapshotSecurityStamp };

        var securityStamp = await store.GetSecurityStampAsync(userSnapshot, CancellationToken.None);

        Assert.Equal(snapshotSecurityStamp, securityStamp);
        Assert.Equal(
            System.Data.ConnectionState.Closed,
            dbContext.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenForUserAsync_PropagatesCancellationAndFingerprintFailures()
    {
        await using (var provider = CreateProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
            var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
            var user = await CreateUserAsync(userManager, "cancelled-generation@example.test", confirmed: true);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.GeneratePasswordResetTokenForUserAsync(
                    user.Id,
                    ComputeFingerprint(user),
                    cancellationSource.Token));
        }

        var cancellationStore = new InMemoryUserStore();
        await using (var provider = CreateProvider(userStore: cancellationStore))
        await using (var scope = provider.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
            var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
            var user = await CreateUserAsync(userManager, "midflight-cancellation@example.test", confirmed: true);
            using var cancellationSource = new CancellationTokenSource();
            cancellationStore.BeforeGetUserId = _ => cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.GeneratePasswordResetTokenForUserAsync(
                    user.Id,
                    ComputeFingerprint(user),
                    cancellationSource.Token));
        }

        var expectedException = new InvalidOperationException("Fingerprint computation failed.");
        await using (var provider = CreateProvider(
            credentialGenerationFingerprint: new ThrowingCredentialGenerationFingerprint(expectedException)))
        await using (var scope = provider.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
            var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
            var user = await CreateUserAsync(userManager, "failed-generation@example.test", confirmed: true);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GeneratePasswordResetTokenForUserAsync(
                    user.Id,
                    new byte[32]));

            Assert.Same(expectedException, exception);
            Assert.DoesNotContain(user.Email!, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(user.SecurityStamp!, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_UsesIdentityAndUpdatesPasswordAndSecurityStamp()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "reset@example.test", confirmed: true);
        var oldSecurityStamp = user.SecurityStamp;
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id, ComputeFingerprint(user));

        var result = await service.ResetPasswordAsync(token.Email, token.Token, NewPassword);
        var updatedUser = await userManager.FindByIdAsync(user.Id.ToString());

        Assert.Equal(PasswordResetStatus.Succeeded, result.Status);
        Assert.True(result.Succeeded);
        Assert.False(await userManager.CheckPasswordAsync(user, OldPassword));
        Assert.True(await userManager.CheckPasswordAsync(user, NewPassword));
        Assert.NotEqual(oldSecurityStamp, updatedUser?.SecurityStamp);
        Assert.True(updatedUser?.EmailConfirmed);
        Assert.DoesNotContain(token.Email!, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token.Token!, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPasswordAsync_RejectsReusedTokenWithoutChangingTheNewPassword()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "reuse@example.test", confirmed: true);
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id, ComputeFingerprint(user));

        var first = await service.ResetPasswordAsync(token.Email, token.Token, NewPassword);
        var second = await service.ResetPasswordAsync(token.Email, token.Token, "Another Password 987!");

        Assert.Equal(PasswordResetStatus.Succeeded, first.Status);
        Assert.Equal(PasswordResetStatus.InvalidRequest, second.Status);
        Assert.True(await userManager.CheckPasswordAsync(user, NewPassword));
        Assert.False(await userManager.CheckPasswordAsync(user, "Another Password 987!"));
        Assert.DoesNotContain(token.Token!, second.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token.Email!, second.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPasswordAsync_RejectsEmailConfirmationToken()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "confirmation-token@example.test", confirmed: true);
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        var result = await service.ResetPasswordAsync(user.Email, token, NewPassword);

        Assert.Equal(PasswordResetStatus.InvalidRequest, result.Status);
        Assert.True(await userManager.CheckPasswordAsync(user, OldPassword));
        Assert.DoesNotContain(token, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPasswordAsync_RejectsInvalidTamperedAndOtherUserTokens()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var firstUser = await CreateUserAsync(userManager, "first@example.test", confirmed: true);
        var otherUser = await CreateUserAsync(userManager, "other@example.test", confirmed: true);
        var token = await userManager.GeneratePasswordResetTokenAsync(firstUser);
        var tamperedToken = (token[0] == 'A' ? "B" : "A") + token[1..];
        var anotherUsersToken = await userManager.GeneratePasswordResetTokenAsync(firstUser);

        var invalid = await service.ResetPasswordAsync(firstUser.Email, "not-a-token", NewPassword);
        var tampered = await service.ResetPasswordAsync(firstUser.Email, tamperedToken, NewPassword);
        var otherUserResult = await service.ResetPasswordAsync(
            otherUser.Email,
            anotherUsersToken,
            NewPassword);

        Assert.Equal(PasswordResetStatus.InvalidRequest, invalid.Status);
        Assert.Equal(PasswordResetStatus.InvalidRequest, tampered.Status);
        Assert.Equal(PasswordResetStatus.InvalidRequest, otherUserResult.Status);
        Assert.True(await userManager.CheckPasswordAsync(firstUser, OldPassword));
        Assert.True(await userManager.CheckPasswordAsync(otherUser, OldPassword));
    }

    [Fact]
    public async Task ResetPasswordAsync_RejectsExpiredToken()
    {
        await using var provider = CreateProvider(TimeSpan.FromTicks(-1));
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "expired@example.test", confirmed: true);
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id, ComputeFingerprint(user));

        var result = await service.ResetPasswordAsync(token.Email, token.Token, NewPassword);

        Assert.Equal(PasswordResetStatus.InvalidRequest, result.Status);
        Assert.True(await userManager.CheckPasswordAsync(user, OldPassword));
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("A" + "a" + "1" + "!" + "b" + "c" + "d" + "e" + "f" + "g" + "h")]
    public async Task ResetPasswordAsync_RejectsIdentityPasswordPolicyFailures(string invalidPassword)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "weak-password@example.test", confirmed: true);
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id, ComputeFingerprint(user));

        var result = await service.ResetPasswordAsync(token.Email, token.Token, invalidPassword);

        Assert.Equal(PasswordResetStatus.InvalidPassword, result.Status);
        Assert.True(await userManager.CheckPasswordAsync(user, OldPassword));
        Assert.DoesNotContain(invalidPassword, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPasswordAsync_RejectsPasswordLongerThan256Characters()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "long-password@example.test", confirmed: true);
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id, ComputeFingerprint(user));

        var result = await service.ResetPasswordAsync(token.Email, token.Token, new string('A', 257));

        Assert.Equal(PasswordResetStatus.InvalidPassword, result.Status);
        Assert.True(await userManager.CheckPasswordAsync(user, OldPassword));
        Assert.DoesNotContain(new string('A', 257), result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPasswordAsync_RejectsUnconfirmedAccount()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "unconfirmed-reset@example.test", confirmed: false);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var result = await service.ResetPasswordAsync(user.Email, token, NewPassword);

        Assert.Equal(PasswordResetStatus.InvalidRequest, result.Status);
        Assert.False(await userManager.CheckPasswordAsync(user, NewPassword));
        Assert.True(await userManager.CheckPasswordAsync(user, OldPassword));
        Assert.False(user.EmailConfirmed);
    }

    private static ServiceProvider CreateProvider(
        TimeSpan? tokenLifespan = null,
        InMemoryUserStore? userStore = null,
        ICredentialGenerationFingerprint? credentialGenerationFingerprint = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = string.Empty,
                ["EmailOutboxWorker:Enabled"] = "false",
                ["ResendEmail:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddInfrastructureServices(configuration);
        services.RemoveAll<IUserStore<XaquiamuniUser>>();
        services.AddSingleton(userStore ?? new InMemoryUserStore());
        services.AddSingleton<IUserStore<XaquiamuniUser>>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryUserStore>());
        services.Replace(ServiceDescriptor.Scoped<IPasswordResetTransactionCoordinator>(
            serviceProvider => new InMemoryPasswordResetTransactionCoordinator(
                serviceProvider.GetRequiredService<InMemoryUserStore>())));
        services.Replace(ServiceDescriptor.Scoped<IPasswordResetOutboxGenerationCancellation>(
            _ => new InMemoryPasswordResetOutboxGenerationCancellation()));
        if (credentialGenerationFingerprint is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(credentialGenerationFingerprint));
        }

        if (tokenLifespan is not null)
        {
            services.Configure<XaquiamuniPasswordResetTokenProviderOptions>(
                options => options.TokenLifespan = tokenLifespan.Value);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static byte[] ComputeFingerprint(XaquiamuniUser user) =>
        new Sha256CredentialGenerationFingerprint().Compute(user.SecurityStamp!);

    private sealed class InMemoryPasswordResetTransactionCoordinator(InMemoryUserStore userStore)
        : IPasswordResetTransactionCoordinator
    {
        public Task<Guid?> FindUserIdByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(userStore.FindUserIdByNormalizedEmail(normalizedEmail));
        }

        public Task<IPasswordResetTransaction> BeginAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IPasswordResetTransaction>(new InMemoryPasswordResetTransaction());
        }
    }

    private sealed class InMemoryPasswordResetTransaction : IPasswordResetTransaction
    {
        public Task<bool> ReloadUserAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryPasswordResetOutboxGenerationCancellation
        : IPasswordResetOutboxGenerationCancellation
    {
        public Task<int> CancelStaleMessagesAsync(
            Guid userId,
            byte[] newGenerationFingerprint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
    }

    private sealed class ThrowingCredentialGenerationFingerprint(Exception exception)
        : ICredentialGenerationFingerprint
    {
        public byte[] Compute(string securityStamp) => throw exception;

        public bool Matches(string securityStamp, ReadOnlySpan<byte> fingerprint) =>
            throw exception;
    }

    private static async Task<XaquiamuniUser> CreateUserAsync(
        UserManager<XaquiamuniUser> userManager,
        string email,
        bool confirmed)
    {
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed
        };
        var result = await userManager.CreateAsync(user, OldPassword);
        Assert.True(result.Succeeded, string.Join(',', result.Errors.Select(error => error.Code)));
        return user;
    }

    private sealed class InMemoryUserStore :
        IUserStore<XaquiamuniUser>,
        IUserEmailStore<XaquiamuniUser>,
        IUserPasswordStore<XaquiamuniUser>,
        IUserSecurityStampStore<XaquiamuniUser>
    {
        private readonly Dictionary<Guid, XaquiamuniUser> _users = [];

        public InMemoryUserStore(bool returnSnapshotOnFindById = false)
        {
            ReturnSnapshotOnFindById = returnSnapshotOnFindById;
        }

        public bool ReturnSnapshotOnFindById { get; }
        public Action<XaquiamuniUser>? BeforeGetUserId { get; set; }
        public XaquiamuniUser? LastUserById { get; private set; }
        public string? LastSecurityStampRead { get; private set; }
        public int GetUserIdCalls { get; private set; }

        public void SetPersistedSecurityStamp(Guid userId, string securityStamp)
        {
            _users[userId].SecurityStamp = securityStamp;
        }

        public Guid? FindUserIdByNormalizedEmail(string normalizedEmail) =>
            _users.Values
                .Where(user => string.Equals(
                    user.NormalizedEmail,
                    normalizedEmail,
                    StringComparison.Ordinal))
                .Select(user => (Guid?)user.Id)
                .SingleOrDefault();

        public Task<IdentityResult> CreateAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (user.Id == Guid.Empty)
            {
                user.Id = Guid.NewGuid();
            }

            _users.Add(user.Id, user);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _users[user.Id] = user;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _users.Remove(user.Id);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<XaquiamuniUser?> FindByIdAsync(
            string userId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = Guid.TryParse(userId, out var id) && _users.TryGetValue(id, out var found)
                ? ReturnSnapshotOnFindById ? CloneUser(found) : found
                : null;
            LastUserById = user;
            return Task.FromResult(user);
        }

        public Task<XaquiamuniUser?> FindByNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_users.Values.FirstOrDefault(user =>
                string.Equals(user.NormalizedUserName, normalizedUserName, StringComparison.Ordinal)));
        }

        public Task<string> GetUserIdAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetUserIdCalls++;
            BeforeGetUserId?.Invoke(user);
            return Task.FromResult(user.Id.ToString());
        }

        public Task<string?> GetUserNameAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.UserName);
        }

        public Task SetUserNameAsync(
            XaquiamuniUser user,
            string? userName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.NormalizedUserName);
        }

        public Task SetNormalizedUserNameAsync(
            XaquiamuniUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetEmailAsync(
            XaquiamuniUser user,
            string? email,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.Email = email;
            return Task.CompletedTask;
        }

        public Task<string?> GetEmailAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.Email);
        }

        public Task<bool> GetEmailConfirmedAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.EmailConfirmed);
        }

        public Task SetEmailConfirmedAsync(
            XaquiamuniUser user,
            bool confirmed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedEmailAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.NormalizedEmail);
        }

        public Task SetNormalizedEmailAsync(
            XaquiamuniUser user,
            string? normalizedEmail,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }

        public Task<XaquiamuniUser?> FindByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_users.Values.FirstOrDefault(user =>
                string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal)));
        }

        public Task SetPasswordHashAsync(
            XaquiamuniUser user,
            string? passwordHash,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.PasswordHash);
        }

        public Task<bool> HasPasswordAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user.PasswordHash is not null);
        }

        public Task SetSecurityStampAsync(
            XaquiamuniUser user,
            string? stamp,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            user.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecurityStampAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSecurityStampRead = user.SecurityStamp;
            return Task.FromResult(user.SecurityStamp);
        }

        private static XaquiamuniUser CloneUser(XaquiamuniUser user) =>
            new()
            {
                Id = user.Id,
                UserName = user.UserName,
                NormalizedUserName = user.NormalizedUserName,
                Email = user.Email,
                NormalizedEmail = user.NormalizedEmail,
                EmailConfirmed = user.EmailConfirmed,
                SecurityStamp = user.SecurityStamp,
                PasswordHash = user.PasswordHash
            };

        public void Dispose()
        {
        }
    }
}
