using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class LoginClienteTests
{
    [Fact]
    public async Task ValidCustomerCredentials_SucceedAndUseLockoutAwareIdentitySignIn()
    {
        var user = new XaquiamuniUser
        {
            Id = Guid.NewGuid(),
            Email = "client@example.test",
            EmailConfirmed = true
        };
        var userManager = new FakeUserManager(user, isCustomer: true);
        var signInManager = new FakeSignInManager(userManager, SignInResult.Success);
        var useCase = CreateUseCase(userManager, signInManager);

        var result = await useCase.ExecuteAsync(new LoginClienteInput
        {
            Email = "  CLIENT@example.test ",
            Senha = "Valid!Password123"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(LoginClienteStatus.Succeeded, result.Status);
        Assert.Equal("CLIENT@example.test", userManager.LastEmail);
        Assert.Same(user, signInManager.LastUser);
        Assert.Equal("Valid!Password123", signInManager.LastPassword);
        Assert.False(signInManager.LastIsPersistent);
        Assert.True(signInManager.LastLockoutOnFailure);
        Assert.Equal(1, signInManager.Calls);
        Assert.Equal(1, signInManager.SignInCalls);
        Assert.Null(signInManager.LastLoginProvider);
        Assert.False(signInManager.LastBypassTwoFactor);
        Assert.True(signInManager.CookieIssued);
    }

    [Fact]
    public async Task InvalidPassword_FailsWithoutIssuingCookie()
    {
        var userManager = new FakeUserManager(new XaquiamuniUser(), isCustomer: true);
        var signInManager = new FakeSignInManager(userManager, SignInResult.Failed);
        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(ValidInput());

        Assert.False(result.Succeeded);
        Assert.Equal(LoginClienteStatus.InvalidCredentials, result.Status);
        Assert.True(signInManager.LastLockoutOnFailure);
        Assert.Equal(1, signInManager.Calls);
        Assert.Equal(0, signInManager.SignInCalls);
        Assert.False(signInManager.CookieIssued);
    }

    [Fact]
    public async Task UnconfirmedCustomerWithValidPassword_ReturnsSpecificStatusWithoutSigningIn()
    {
        var userManager = new FakeUserManager(
            new XaquiamuniUser { EmailConfirmed = false },
            isCustomer: true);
        var signInManager = new FakeSignInManager(userManager, SignInResult.Success);

        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(ValidInput());

        Assert.Equal(LoginClienteStatus.EmailNotConfirmed, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(1, signInManager.Calls);
        Assert.Equal(0, signInManager.SignInCalls);
        Assert.False(signInManager.CookieIssued);
    }

    [Fact]
    public async Task UnconfirmedCustomerWithInvalidPassword_RemainsGenericFailure()
    {
        var userManager = new FakeUserManager(
            new XaquiamuniUser { EmailConfirmed = false },
            isCustomer: true);
        var signInManager = new FakeSignInManager(userManager, SignInResult.Failed);

        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(ValidInput());

        Assert.Equal(LoginClienteStatus.InvalidCredentials, result.Status);
        Assert.Equal(1, signInManager.Calls);
        Assert.Equal(0, signInManager.SignInCalls);
        Assert.False(signInManager.CookieIssued);
    }

    [Fact]
    public async Task UnknownEmail_FailsWithoutAttemptingSignIn()
    {
        var userManager = new FakeUserManager(user: null, isCustomer: false);
        var signInManager = new FakeSignInManager(userManager, SignInResult.Success);
        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(ValidInput());

        Assert.False(result.Succeeded);
        Assert.Equal(LoginClienteStatus.InvalidCredentials, result.Status);
        Assert.Equal(0, signInManager.Calls);
        Assert.False(signInManager.CookieIssued);
    }

    [Fact]
    public async Task LockedAccount_FailsWithoutIssuingCookie()
    {
        var userManager = new FakeUserManager(new XaquiamuniUser(), isCustomer: true);
        var signInManager = new FakeSignInManager(userManager, SignInResult.LockedOut);
        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(ValidInput());

        Assert.False(result.Succeeded);
        Assert.Equal(LoginClienteStatus.InvalidCredentials, result.Status);
        Assert.False(signInManager.CookieIssued);
    }

    [Fact]
    public async Task UserWithoutCustomerRole_FailsBeforePasswordSignIn()
    {
        var userManager = new FakeUserManager(new XaquiamuniUser(), isCustomer: false);
        var signInManager = new FakeSignInManager(userManager, SignInResult.Success);
        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(ValidInput());

        Assert.False(result.Succeeded);
        Assert.Equal(LoginClienteStatus.InvalidCredentials, result.Status);
        Assert.Equal(XaquiamuniRoles.Cliente, userManager.LastRole);
        Assert.Equal(0, signInManager.Calls);
        Assert.False(signInManager.CookieIssued);
    }

    [Theory]
    [InlineData(null, "SomePassword!123")]
    [InlineData("", "SomePassword!123")]
    [InlineData("client@example.test", null)]
    [InlineData("client@example.test", "   ")]
    public async Task MissingCredentials_FailWithoutCallingIdentity(string? email, string? password)
    {
        var userManager = new FakeUserManager(user: null, isCustomer: false);
        var signInManager = new FakeSignInManager(userManager, SignInResult.Success);
        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(new LoginClienteInput { Email = email, Senha = password });

        Assert.False(result.Succeeded);
        Assert.Equal(LoginClienteStatus.InvalidCredentials, result.Status);
        Assert.Null(userManager.LastEmail);
        Assert.Equal(0, signInManager.Calls);
        Assert.False(signInManager.CookieIssued);
    }

    [Fact]
    public async Task InvalidSignInResult_RemainsGenericFailure()
    {
        var userManager = new FakeUserManager(new XaquiamuniUser(), isCustomer: true);
        var signInManager = new FakeSignInManager(userManager, SignInResult.NotAllowed);
        var result = await CreateUseCase(userManager, signInManager)
            .ExecuteAsync(ValidInput());

        Assert.False(result.Succeeded);
        Assert.Equal(LoginClienteStatus.InvalidCredentials, result.Status);
        Assert.False(signInManager.CookieIssued);
    }

    private static LoginClienteInput ValidInput() => new()
    {
        Email = "client@example.test",
        Senha = "SomePassword!123"
    };

    private static LoginCliente CreateUseCase(
        FakeUserManager userManager,
        FakeSignInManager signInManager) =>
        new(new IdentityClienteLoginService(userManager, signInManager));

    private sealed class FakeUserManager : UserManager<XaquiamuniUser>
    {
        private readonly XaquiamuniUser? _user;
        private readonly bool _isCustomer;

        public FakeUserManager(XaquiamuniUser? user, bool isCustomer)
            : base(
                new TestUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<XaquiamuniUser>(),
                Array.Empty<IUserValidator<XaquiamuniUser>>(),
                Array.Empty<IPasswordValidator<XaquiamuniUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<UserManager<XaquiamuniUser>>.Instance)
        {
            _user = user;
            _isCustomer = isCustomer;
        }

        public string? LastEmail { get; private set; }
        public string? LastRole { get; private set; }

        public override Task<XaquiamuniUser?> FindByEmailAsync(string email)
        {
            LastEmail = email;
            return Task.FromResult(_user);
        }

        public override Task<bool> IsInRoleAsync(XaquiamuniUser user, string role)
        {
            LastRole = role;
            return Task.FromResult(_isCustomer);
        }

        public override Task<bool> IsEmailConfirmedAsync(XaquiamuniUser user) =>
            Task.FromResult(user.EmailConfirmed);
    }

    private sealed class FakeSignInManager : XaquiamuniSignInManager
    {
        private readonly SignInResult _passwordCheckResult;
        private readonly SignInResult _signInResult;

        public FakeSignInManager(
            UserManager<XaquiamuniUser> userManager,
            SignInResult passwordCheckResult,
            SignInResult? signInResult = null)
            : base(
                userManager,
                new HttpContextAccessor(),
                new TestClaimsPrincipalFactory(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<XaquiamuniUser>>.Instance,
                new AuthenticationSchemeProvider(
                    Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
                new TestUserConfirmation())
        {
            _passwordCheckResult = passwordCheckResult;
            _signInResult = signInResult ?? passwordCheckResult;
        }

        public int Calls { get; private set; }
        public int SignInCalls { get; private set; }
        public XaquiamuniUser? LastUser { get; private set; }
        public string? LastPassword { get; private set; }
        public string? LastLoginProvider { get; private set; }
        public bool LastIsPersistent { get; private set; }
        public bool LastBypassTwoFactor { get; private set; }
        public bool LastLockoutOnFailure { get; private set; }
        public bool CookieIssued { get; private set; }

        public override Task<SignInResult> CheckPasswordSignInAsync(
            XaquiamuniUser user,
            string password,
            bool lockoutOnFailure)
        {
            Calls++;
            LastUser = user;
            LastPassword = password;
            LastLockoutOnFailure = lockoutOnFailure;
            return Task.FromResult(_passwordCheckResult);
        }

        protected override Task<SignInResult> SignInOrTwoFactorAsync(
            XaquiamuniUser user,
            bool isPersistent,
            string? loginProvider = null,
            bool bypassTwoFactor = false)
        {
            SignInCalls++;
            LastUser = user;
            LastLoginProvider = loginProvider;
            LastIsPersistent = isPersistent;
            LastBypassTwoFactor = bypassTwoFactor;
            CookieIssued = _signInResult.Succeeded;
            return Task.FromResult(_signInResult);
        }
    }

    private sealed class TestUserStore : IUserStore<XaquiamuniUser>
    {
        public Task<IdentityResult> CreateAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }

        public Task<XaquiamuniUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<XaquiamuniUser?>(null);

        public Task<XaquiamuniUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult<XaquiamuniUser?>(null);

        public Task<string?> GetNormalizedUserNameAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task<string> GetUserIdAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(
            XaquiamuniUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(XaquiamuniUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(XaquiamuniUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
    }

    private sealed class TestClaimsPrincipalFactory : IUserClaimsPrincipalFactory<XaquiamuniUser>
    {
        public Task<System.Security.Claims.ClaimsPrincipal> CreateAsync(XaquiamuniUser user) =>
            Task.FromResult(new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("test")));
    }

    private sealed class TestUserConfirmation : IUserConfirmation<XaquiamuniUser>
    {
        public Task<bool> IsConfirmedAsync(UserManager<XaquiamuniUser> manager, XaquiamuniUser user) =>
            Task.FromResult(true);
    }
}
