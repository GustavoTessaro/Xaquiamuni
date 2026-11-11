using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class IdentitySignInConfigurationTests
{
    [Fact]
    public async Task SignInManager_UsesTheConfiguredCookieScheme()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var signInManager = scope.ServiceProvider
            .GetRequiredService<SignInManager<XaquiamuniUser>>();
        var schemeProvider = scope.ServiceProvider
            .GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();

        Assert.Equal(AuthenticationConfiguration.CookieScheme, signInManager.AuthenticationScheme);
        Assert.Equal(AuthenticationConfiguration.CookieScheme, defaultScheme?.Name);
    }

    [Fact]
    public async Task Identity_UsesOneSecureCookieAndConfiguredLockoutPolicy()
    {
        using var factory = CreateFactory();
        var services = factory.Services;
        var identityOptions = services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var cookieOptions = services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationConfiguration.CookieScheme);
        var schemes = await services
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        var cookieSchemes = schemes
            .Where(scheme => typeof(CookieAuthenticationHandler).IsAssignableFrom(scheme.HandlerType))
            .Select(scheme => scheme.Name)
            .ToArray();

        Assert.Equal(AuthenticationConfiguration.CookieName, cookieOptions.Cookie.Name);
        Assert.True(cookieOptions.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookieOptions.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, cookieOptions.Cookie.SameSite);
        Assert.Equal("/", cookieOptions.Cookie.Path);
        Assert.Equal(TimeSpan.FromHours(8), cookieOptions.ExpireTimeSpan);
        Assert.Equal([AuthenticationConfiguration.CookieScheme], cookieSchemes);
        Assert.Equal(5, identityOptions.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), identityOptions.Lockout.DefaultLockoutTimeSpan);
        Assert.True(identityOptions.Lockout.AllowedForNewUsers);
        Assert.False(identityOptions.SignIn.RequireConfirmedEmail);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] =
                        "Server=localhost;Database=XaquiamuniIdentityConfigurationTests;Integrated Security=True;TrustServerCertificate=True"
                })));
}
