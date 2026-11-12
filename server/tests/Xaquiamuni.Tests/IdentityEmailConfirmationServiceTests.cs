using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class IdentityEmailConfirmationServiceTests
{
    [Fact]
    public async Task FindUnconfirmedUserIdByEmailAsync_ForEligibleUserUsesIdentityEmailNormalization()
    {
        var userId = Guid.NewGuid();
        var user = new XaquiamuniUser
        {
            Id = userId,
            Email = "Client@Example.invalid",
            EmailConfirmed = false,
            SecurityStamp = "unchanged-security-stamp"
        };
        var userManager = new FakeUserManager(user);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.FindUnconfirmedUserIdByEmailAsync("  client@example.invalid  ");

        Assert.Equal(userId, result);
        Assert.Equal("client@example.invalid", userManager.LastEmail);
        Assert.Equal("CLIENT@EXAMPLE.INVALID", userManager.LastNormalizedEmail);
        Assert.Equal(0, userManager.GenerateTokenCalls);
        Assert.Equal(0, userManager.ConfirmTokenCalls);
        Assert.Equal(0, userManager.PersistedMutationCalls);
        Assert.Equal("Client@Example.invalid", user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("unchanged-security-stamp", user.SecurityStamp);
    }

    [Fact]
    public async Task FindUnconfirmedUserIdByEmailAsync_ForMissingUserReturnsNull()
    {
        var userManager = new FakeUserManager(user: null);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.FindUnconfirmedUserIdByEmailAsync("client@example.invalid");

        Assert.Null(result);
        Assert.Equal(0, userManager.GenerateTokenCalls);
        Assert.Equal(0, userManager.PersistedMutationCalls);
    }

    [Fact]
    public async Task FindUnconfirmedUserIdByEmailAsync_ForConfirmedUserReturnsNull()
    {
        var user = new XaquiamuniUser
        {
            Id = Guid.NewGuid(),
            Email = "client@example.invalid",
            EmailConfirmed = true
        };
        var userManager = new FakeUserManager(user);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.FindUnconfirmedUserIdByEmailAsync("client@example.invalid");

        Assert.Null(result);
        Assert.Equal(0, userManager.GenerateTokenCalls);
        Assert.Equal(0, userManager.PersistedMutationCalls);
        Assert.True(user.EmailConfirmed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("client@example..invalid")]
    public async Task FindUnconfirmedUserIdByEmailAsync_ForInvalidEmailReturnsNull(string? email)
    {
        var user = new XaquiamuniUser
        {
            Id = Guid.NewGuid(),
            Email = "client@example.invalid",
            EmailConfirmed = false
        };
        var userManager = new FakeUserManager(user);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.FindUnconfirmedUserIdByEmailAsync(email!);

        Assert.Null(result);
        Assert.Null(userManager.LastEmail);
        Assert.Equal(0, userManager.GenerateTokenCalls);
        Assert.Equal(0, userManager.PersistedMutationCalls);
    }

    [Fact]
    public async Task FindUnconfirmedUserIdByEmailAsync_ForUserWithoutValidEmailReturnsNull()
    {
        var user = new XaquiamuniUser
        {
            Id = Guid.NewGuid(),
            Email = "not-an-email",
            EmailConfirmed = false
        };
        var userManager = new FakeUserManager(user, returnUserForAnyEmail: true);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.FindUnconfirmedUserIdByEmailAsync("client@example.invalid");

        Assert.Null(result);
        Assert.Equal(0, userManager.GenerateTokenCalls);
        Assert.Equal(0, userManager.PersistedMutationCalls);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenAsync_ForUnconfirmedUserReturnsToken()
    {
        var user = new XaquiamuniUser { Email = "client@example.invalid", EmailConfirmed = false };
        var userManager = new FakeUserManager(user);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.GenerateEmailConfirmationTokenAsync(" client@example.invalid ");

        Assert.Equal(EmailConfirmationTokenStatus.Generated, result.Status);
        Assert.Equal(FakeUserManager.TestToken, result.Token);
        Assert.Equal("client@example.invalid", userManager.LastEmail);
        Assert.Equal(1, userManager.GenerateTokenCalls);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenAsync_ForMissingUserReturnsSafeNotFound()
    {
        var userManager = new FakeUserManager(user: null);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.GenerateEmailConfirmationTokenAsync("client@example.invalid");

        Assert.Equal(EmailConfirmationTokenStatus.UserNotFound, result.Status);
        Assert.Null(result.Token);
        Assert.Equal(0, userManager.GenerateTokenCalls);
        Assert.DoesNotContain("client@example.invalid", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenAsync_ForConfirmedUserDoesNotGenerateToken()
    {
        var userManager = new FakeUserManager(
            new XaquiamuniUser { Email = "client@example.invalid", EmailConfirmed = true });
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.GenerateEmailConfirmationTokenAsync("client@example.invalid");

        Assert.Equal(EmailConfirmationTokenStatus.AlreadyConfirmed, result.Status);
        Assert.Null(result.Token);
        Assert.Equal(0, userManager.GenerateTokenCalls);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenForUserAsync_ReturnsCurrentEmailAndToken()
    {
        var userId = Guid.NewGuid();
        var user = new XaquiamuniUser
        {
            Id = userId,
            Email = "current@example.invalid",
            EmailConfirmed = false
        };
        var userManager = new FakeUserManager(user);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.GenerateEmailConfirmationTokenForUserAsync(userId);

        Assert.Equal(EmailConfirmationTokenStatus.Generated, result.Status);
        Assert.Equal("current@example.invalid", result.Email);
        Assert.Equal(FakeUserManager.TestToken, result.Token);
        Assert.Equal(userId.ToString(), userManager.LastUserId);
        Assert.Equal(1, userManager.GenerateTokenCalls);
        Assert.DoesNotContain("current@example.invalid", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(FakeUserManager.TestToken, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenForUserAsync_ForMissingUserDoesNotGenerateToken()
    {
        var userManager = new FakeUserManager(user: null);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.GenerateEmailConfirmationTokenForUserAsync(Guid.NewGuid());

        Assert.Equal(EmailConfirmationTokenStatus.UserNotFound, result.Status);
        Assert.Null(result.Email);
        Assert.Null(result.Token);
        Assert.Equal(0, userManager.GenerateTokenCalls);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenForUserAsync_ForUserWithoutEmailDoesNotGenerateToken()
    {
        var userId = Guid.NewGuid();
        var userManager = new FakeUserManager(
            new XaquiamuniUser { Id = userId, Email = null, EmailConfirmed = false });
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.GenerateEmailConfirmationTokenForUserAsync(userId);

        Assert.Equal(EmailConfirmationTokenStatus.EmailMissing, result.Status);
        Assert.Null(result.Email);
        Assert.Null(result.Token);
        Assert.Equal(0, userManager.GenerateTokenCalls);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenForUserAsync_ForConfirmedUserDoesNotGenerateToken()
    {
        var userId = Guid.NewGuid();
        var userManager = new FakeUserManager(
            new XaquiamuniUser
            {
                Id = userId,
                Email = "current@example.invalid",
                EmailConfirmed = true
            });
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.GenerateEmailConfirmationTokenForUserAsync(userId);

        Assert.Equal(EmailConfirmationTokenStatus.AlreadyConfirmed, result.Status);
        Assert.Null(result.Email);
        Assert.Null(result.Token);
        Assert.Equal(0, userManager.GenerateTokenCalls);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WithValidTokenConfirmsUser()
    {
        var user = new XaquiamuniUser { Email = "client@example.invalid", EmailConfirmed = false };
        var userManager = new FakeUserManager(user);
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.ConfirmEmailAsync("client@example.invalid", FakeUserManager.TestToken);

        Assert.Equal(EmailConfirmationStatus.Confirmed, result.Status);
        Assert.True(result.Succeeded);
        Assert.True(user.EmailConfirmed);
        Assert.Equal(1, userManager.ConfirmTokenCalls);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WithInvalidOrExpiredTokenReturnsSafeFailure()
    {
        const string privateDescription = "private token and email details";
        var userManager = new FakeUserManager(
            new XaquiamuniUser { Email = "client@example.invalid", EmailConfirmed = false },
            IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = privateDescription }));
        var service = new IdentityEmailConfirmationService(userManager);

        var result = await service.ConfirmEmailAsync("client@example.invalid", "invalid-test-token");

        Assert.Equal(EmailConfirmationStatus.InvalidOrExpiredToken, result.Status);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain(privateDescription, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("invalid-test-token", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("client@example.invalid", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WhenReusedAfterConfirmationReturnsAlreadyConfirmed()
    {
        var user = new XaquiamuniUser { Email = "client@example.invalid", EmailConfirmed = false };
        var userManager = new FakeUserManager(user);
        var service = new IdentityEmailConfirmationService(userManager);

        var firstResult = await service.ConfirmEmailAsync("client@example.invalid", FakeUserManager.TestToken);
        var secondResult = await service.ConfirmEmailAsync("client@example.invalid", FakeUserManager.TestToken);

        Assert.Equal(EmailConfirmationStatus.Confirmed, firstResult.Status);
        Assert.Equal(EmailConfirmationStatus.AlreadyConfirmed, secondResult.Status);
        Assert.Equal(1, userManager.ConfirmTokenCalls);
        Assert.DoesNotContain(FakeUserManager.TestToken, secondResult.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenAsync_PropagatesInternalFailureWithoutLoggingSensitiveValues()
    {
        var userManager = new FakeUserManager(user: null)
        {
            FindUserException = new InvalidOperationException("Internal test failure")
        };
        var service = new IdentityEmailConfirmationService(userManager);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmailConfirmationTokenAsync("client@example.invalid"));

        Assert.Equal("Internal test failure", exception.Message);
        Assert.Equal(0, userManager.GenerateTokenCalls);
    }

    private sealed class FakeUserManager : UserManager<XaquiamuniUser>
    {
        private readonly XaquiamuniUser? _user;
        private readonly FakeUserStore _userStore;
        private readonly IdentityResult? _confirmationResult;

        public FakeUserManager(
            XaquiamuniUser? user,
            IdentityResult? confirmationResult = null,
            bool returnUserForAnyEmail = false)
            : this(
                user,
                confirmationResult,
                new FakeUserStore(user, returnUserForAnyEmail))
        {
        }

        private FakeUserManager(
            XaquiamuniUser? user,
            IdentityResult? confirmationResult,
            FakeUserStore userStore)
            : base(
                userStore,
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<XaquiamuniUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<UserManager<XaquiamuniUser>>.Instance)
        {
            _user = user;
            _confirmationResult = confirmationResult;
            _userStore = userStore;
        }

        public const string TestToken = "test-only-confirmation-token";

        public Exception? FindUserException { get; init; }
        public int GenerateTokenCalls { get; private set; }
        public int ConfirmTokenCalls { get; private set; }
        public string? LastEmail { get; private set; }
        public string? LastUserId { get; private set; }
        public string? LastNormalizedEmail => _userStore.LastNormalizedEmail;
        public int PersistedMutationCalls => _userStore.PersistedMutationCalls;

        public override Task<XaquiamuniUser?> FindByEmailAsync(string email)
        {
            if (FindUserException is not null)
            {
                return Task.FromException<XaquiamuniUser?>(FindUserException);
            }

            LastEmail = email;
            return base.FindByEmailAsync(email);
        }

        public override Task<XaquiamuniUser?> FindByIdAsync(string userId)
        {
            if (FindUserException is not null)
            {
                return Task.FromException<XaquiamuniUser?>(FindUserException);
            }

            LastUserId = userId;
            return Task.FromResult(
                _user is not null && string.Equals(_user.Id.ToString(), userId, StringComparison.Ordinal)
                    ? _user
                    : null);
        }

        public override Task<bool> IsEmailConfirmedAsync(XaquiamuniUser user) =>
            Task.FromResult(user.EmailConfirmed);

        public override Task<string> GenerateEmailConfirmationTokenAsync(XaquiamuniUser user)
        {
            GenerateTokenCalls++;
            return Task.FromResult(TestToken);
        }

        public override Task<IdentityResult> ConfirmEmailAsync(XaquiamuniUser user, string token)
        {
            ConfirmTokenCalls++;
            if (token != TestToken || _confirmationResult is { Succeeded: false })
            {
                return Task.FromResult(_confirmationResult ?? IdentityResult.Failed(
                    new IdentityError { Code = "InvalidToken", Description = "Not exposed" }));
            }

            user.EmailConfirmed = true;
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class FakeUserStore(
        XaquiamuniUser? user,
        bool returnUserForAnyEmail = false) : IUserEmailStore<XaquiamuniUser>
    {
        public string? LastNormalizedEmail { get; private set; }
        public int PersistedMutationCalls { get; private set; }

        public void Dispose() { }
        public Task<string> GetUserIdAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);
        public Task SetUserNameAsync(XaquiamuniUser user, string? userName, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            user.UserName = userName;
            return Task.CompletedTask;
        }
        public Task<string?> GetNormalizedUserNameAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(XaquiamuniUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }
        public Task<IdentityResult> CreateAsync(XaquiamuniUser user, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            return Task.FromResult(IdentityResult.Success);
        }
        public Task<IdentityResult> UpdateAsync(XaquiamuniUser user, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            return Task.FromResult(IdentityResult.Success);
        }
        public Task<IdentityResult> DeleteAsync(XaquiamuniUser user, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            return Task.FromResult(IdentityResult.Success);
        }
        public Task<string?> GetEmailAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Email);
        public Task SetEmailAsync(XaquiamuniUser user, string? email, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            user.Email = email;
            return Task.CompletedTask;
        }
        public Task<bool> GetEmailConfirmedAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.EmailConfirmed);
        public Task SetEmailConfirmedAsync(XaquiamuniUser user, bool confirmed, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }
        public Task<string?> GetNormalizedEmailAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedEmail);
        public Task SetNormalizedEmailAsync(XaquiamuniUser user, string? normalizedEmail, CancellationToken cancellationToken)
        {
            PersistedMutationCalls++;
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }
        public Task<XaquiamuniUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            LastNormalizedEmail = normalizedEmail;
            return Task.FromResult(
                user is not null
                && (returnUserForAnyEmail
                    || string.Equals(
                        user.Email?.ToUpperInvariant(),
                        normalizedEmail,
                        StringComparison.Ordinal))
                    ? user
                    : null);
        }
        public Task<XaquiamuniUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<XaquiamuniUser?>(null);
        public Task<XaquiamuniUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult<XaquiamuniUser?>(null);
    }
}
