using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class IdentityEmailConfirmationServiceTests
{
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

    private sealed class FakeUserManager(
        XaquiamuniUser? user,
        IdentityResult? confirmationResult = null)
        : UserManager<XaquiamuniUser>(
            new FakeUserStore(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<XaquiamuniUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<XaquiamuniUser>>.Instance)
    {
        public const string TestToken = "test-only-confirmation-token";

        public Exception? FindUserException { get; init; }
        public int GenerateTokenCalls { get; private set; }
        public int ConfirmTokenCalls { get; private set; }
        public string? LastEmail { get; private set; }
        public string? LastUserId { get; private set; }

        public override Task<XaquiamuniUser?> FindByEmailAsync(string email)
        {
            if (FindUserException is not null)
            {
                return Task.FromException<XaquiamuniUser?>(FindUserException);
            }

            LastEmail = email;
            return Task.FromResult(user);
        }

        public override Task<XaquiamuniUser?> FindByIdAsync(string userId)
        {
            if (FindUserException is not null)
            {
                return Task.FromException<XaquiamuniUser?>(FindUserException);
            }

            LastUserId = userId;
            return Task.FromResult(
                user is not null && string.Equals(user.Id.ToString(), userId, StringComparison.Ordinal)
                    ? user
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
            if (token != TestToken || confirmationResult is { Succeeded: false })
            {
                return Task.FromResult(confirmationResult ?? IdentityResult.Failed(
                    new IdentityError { Code = "InvalidToken", Description = "Not exposed" }));
            }

            user.EmailConfirmed = true;
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class FakeUserStore : IUserStore<XaquiamuniUser>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);
        public Task SetUserNameAsync(XaquiamuniUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }
        public Task<string?> GetNormalizedUserNameAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(XaquiamuniUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }
        public Task<IdentityResult> CreateAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
        public Task<XaquiamuniUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<XaquiamuniUser?>(null);
        public Task<XaquiamuniUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult<XaquiamuniUser?>(null);
    }
}
