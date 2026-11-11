using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class IdentityDiagnosticCodeSanitizerTests
{
    [Theory]
    [InlineData("PasswordTooShort")]
    [InlineData("PasswordRequiresDigit")]
    [InlineData("PasswordRequiresUpper")]
    [InlineData("PasswordRequiresLower")]
    [InlineData("PasswordRequiresNonAlphanumeric")]
    [InlineData("PasswordRequiresUniqueChars")]
    [InlineData("InvalidUserName")]
    [InlineData("InvalidEmail")]
    public void Sanitize_PreservesOnlyExplicitlyAllowedCodes(string code)
    {
        var result = CadastroClienteDiagnostics.SanitizeIdentityErrorCodes([code]);

        Assert.Equal([code], result);
    }

    [Fact]
    public void Sanitize_ReplacesUnknownCodesAndNeverReturnsDescriptions()
    {
        const string personalData = "person@example.invalid";
        const string secret = "password-value";
        var result = CadastroClienteDiagnostics.SanitizeIdentityErrorCodes(
        [
            personalData,
            "CustomProviderError"
        ]);

        Assert.Equal(["UNKNOWN_IDENTITY_ERROR"], result);
        Assert.DoesNotContain(personalData, string.Join(',', result), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join(',', result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DuplicateEmail")]
    [InlineData("DuplicateUserName")]
    public async Task CreateUserAsync_PreservesDuplicateEmailBehavior(string duplicateCode)
    {
        var service = CreateService(IdentityResult.Failed(
            new IdentityError { Code = duplicateCode, Description = "Sensitive description" }));

        var result = await service.CreateUserAsync(Guid.NewGuid(), "user@example.invalid", "phone", "password");

        Assert.Equal(ClienteIdentityOperationStatus.DuplicateEmail, result.Status);
        Assert.Empty(result.SafeDiagnosticCodes);
    }

    [Theory]
    [InlineData("PasswordTooShort")]
    [InlineData("PasswordRequiresDigit")]
    [InlineData("PasswordRequiresUpper")]
    [InlineData("PasswordRequiresLower")]
    [InlineData("PasswordRequiresNonAlphanumeric")]
    [InlineData("PasswordRequiresUniqueChars")]
    public async Task CreateUserAsync_MapsPasswordErrorsToValidationFailure(string passwordErrorCode)
    {
        var service = CreateService(IdentityResult.Failed(
            new IdentityError { Code = passwordErrorCode, Description = "Sensitive description" }));

        var result = await service.CreateUserAsync(Guid.NewGuid(), "user@example.invalid", "phone", "password");

        Assert.Equal(ClienteIdentityOperationStatus.PasswordValidationFailed, result.Status);
        Assert.Equal([passwordErrorCode], result.SafeDiagnosticCodes);
        Assert.DoesNotContain("Sensitive description", string.Join(',', result.SafeDiagnosticCodes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateUserAsync_DoesNotMapUnknownOrNonPasswordErrorsToValidationFailure()
    {
        var service = CreateService(IdentityResult.Failed(
            new IdentityError { Code = "InvalidEmail", Description = "Sensitive description" },
            new IdentityError { Code = "UnknownCustomCode", Description = "Sensitive description" }));

        var result = await service.CreateUserAsync(Guid.NewGuid(), "user@example.invalid", "phone", "password");

        Assert.Equal(ClienteIdentityOperationStatus.Failed, result.Status);
        Assert.Equal(["InvalidEmail", "UNKNOWN_IDENTITY_ERROR"], result.SafeDiagnosticCodes);
    }

    [Fact]
    public async Task CreateUserAsync_ReturnsSanitizedCodesWithoutIdentityDescriptions()
    {
        const string sensitiveDescription = "private@example.invalid and secret password";
        var service = CreateService(IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = sensitiveDescription },
            new IdentityError { Code = "UnknownCustomCode", Description = sensitiveDescription }));

        var result = await service.CreateUserAsync(Guid.NewGuid(), "user@example.invalid", "phone", "password");

        Assert.Equal(ClienteIdentityOperationStatus.Failed, result.Status);
        Assert.Equal(["PasswordTooShort", "UNKNOWN_IDENTITY_ERROR"], result.SafeDiagnosticCodes);
        Assert.DoesNotContain(sensitiveDescription, string.Join(',', result.SafeDiagnosticCodes), StringComparison.Ordinal);
    }

    private static IdentityClienteIdentityService CreateService(IdentityResult createResult)
    {
        var userManager = new FakeUserManager(createResult);
        var roleManager = new RoleManager<IdentityRole<Guid>>(
            new FakeRoleStore(),
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole<Guid>>>.Instance);
        return new IdentityClienteIdentityService(userManager, roleManager);
    }

    private sealed class FakeUserManager(IdentityResult createResult)
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
        public override Task<IdentityResult> CreateAsync(XaquiamuniUser user, string password) =>
            Task.FromResult(createResult);
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

    private sealed class FakeRoleStore : IRoleStore<IdentityRole<Guid>>
    {
        public void Dispose() { }
        public Task<IdentityResult> CreateAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
        public Task<IdentityRole<Guid>?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
            Task.FromResult<IdentityRole<Guid>?>(null);
        public Task<IdentityRole<Guid>?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
            Task.FromResult<IdentityRole<Guid>?>(null);
        public Task<string> GetRoleIdAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
            Task.FromResult(role.Id.ToString());
        public Task<string?> GetRoleNameAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
            Task.FromResult(role.Name);
        public Task SetRoleNameAsync(IdentityRole<Guid> role, string? roleName, CancellationToken cancellationToken)
        {
            role.Name = roleName;
            return Task.CompletedTask;
        }
        public Task<string?> GetNormalizedRoleNameAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
            Task.FromResult(role.NormalizedName);
        public Task SetNormalizedRoleNameAsync(IdentityRole<Guid> role, string? normalizedName, CancellationToken cancellationToken)
        {
            role.NormalizedName = normalizedName;
            return Task.CompletedTask;
        }
    }
}
