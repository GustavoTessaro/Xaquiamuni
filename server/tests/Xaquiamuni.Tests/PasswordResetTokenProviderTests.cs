using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class PasswordResetTokenProviderTests
{
    [Fact]
    public async Task GeneratePasswordResetToken_IsAcceptedForPasswordResetPurpose()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = CreateUser();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var isValid = await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            token);

        Assert.True(isValid);
        Assert.InRange(token.Length, 1, ResetPasswordRequest.TokenMaxLength);
        Assert.Matches("^[A-Za-z0-9+/]+={0,2}$", token);
    }

    [Fact]
    public async Task PasswordResetProvider_RejectsExpiredTokenWithExpiredTestLifetime()
    {
        using var factory = CreateFactory(TimeSpan.FromTicks(-1));
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = CreateUser();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var isValid = await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            token);

        Assert.False(isValid);
    }

    [Fact]
    public async Task EmailConfirmationToken_IsNotAcceptedForPasswordReset()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = CreateUser();

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var isValid = await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            token);

        Assert.False(isValid);
    }

    [Fact]
    public async Task PasswordResetToken_IsNotAcceptedForEmailConfirmation()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = CreateUser();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var isValid = await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<XaquiamuniUser>.ConfirmEmailTokenPurpose,
            token);

        Assert.False(isValid);
    }

    [Fact]
    public async Task PasswordResetProvider_RejectsTamperedToken()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = CreateUser();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var tamperedToken = (token[0] == 'A' ? "B" : "A") + token[1..];

        var isValid = await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            tamperedToken);

        Assert.False(isValid);
    }

    [Fact]
    public async Task PasswordResetProvider_RejectsTokenForAnotherUser()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = CreateUser();
        var otherUser = CreateUser(user.SecurityStamp);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var isValid = await userManager.VerifyUserTokenAsync(
            otherUser,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<XaquiamuniUser>.ResetPasswordTokenPurpose,
            token);

        Assert.False(isValid);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        TimeSpan? passwordResetTokenLifespan = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = string.Empty
                }))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                if (passwordResetTokenLifespan is not null)
                {
                    services.Configure<XaquiamuniPasswordResetTokenProviderOptions>(
                        options => options.TokenLifespan = passwordResetTokenLifespan.Value);
                }
            }));

    private static XaquiamuniUser CreateUser(string? securityStamp = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = "reset-token-test@example.invalid",
            Email = "reset-token-test@example.invalid",
            SecurityStamp = securityStamp ?? Guid.NewGuid().ToString("N")
        };
}
