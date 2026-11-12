using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Identidade;

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
    public async Task FindEligibleUserIdByEmailAsync_ReturnsOnlyConfirmedUsersWithoutMutatingThem()
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

        var eligible = await service.FindEligibleUserIdByEmailAsync("  CONFIRMED@example.test ");
        var unconfirmed = await service.FindEligibleUserIdByEmailAsync("Unconfirmed@example.test");
        var missing = await service.FindEligibleUserIdByEmailAsync("missing@example.test");
        var invalid = await service.FindEligibleUserIdByEmailAsync("not-an-email");

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

        var generated = await service.GeneratePasswordResetTokenForUserAsync(confirmedUser.Id);
        var unconfirmed = await service.GeneratePasswordResetTokenForUserAsync(unconfirmedUser.Id);
        var missing = await service.GeneratePasswordResetTokenForUserAsync(Guid.NewGuid());

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
    public async Task ResetPasswordAsync_UsesIdentityAndUpdatesPasswordAndSecurityStamp()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var user = await CreateUserAsync(userManager, "reset@example.test", confirmed: true);
        var oldSecurityStamp = user.SecurityStamp;
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id);

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
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id);

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
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id);

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
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id);

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
        var token = await service.GeneratePasswordResetTokenForUserAsync(user.Id);

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

    private static ServiceProvider CreateProvider(TimeSpan? tokenLifespan = null)
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
        services.AddSingleton<InMemoryUserStore>();
        services.AddSingleton<IUserStore<XaquiamuniUser>>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryUserStore>());
        if (tokenLifespan is not null)
        {
            services.Configure<XaquiamuniPasswordResetTokenProviderOptions>(
                options => options.TokenLifespan = tokenLifespan.Value);
        }

        return services.BuildServiceProvider(validateScopes: true);
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
            return Task.FromResult(
                Guid.TryParse(userId, out var id) && _users.TryGetValue(id, out var user)
                    ? user
                    : null);
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
            return Task.FromResult(user.SecurityStamp);
        }

        public void Dispose()
        {
        }
    }
}
