using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public class CookieAuthenticationTests
{
    [Fact]
    public async Task UnauthenticatedRequestToProtectedEndpoint_Returns401WithoutRedirect()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/_test/authenticated");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task AuthenticatedCustomerWithoutAdministrativePermission_Returns403()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var signInResponse = await client.PostAsync(
            $"/_test/sign-in/{XaquiamuniRoles.Cliente}",
            content: null);
        using var response = await client.GetAsync("/_test/admin");

        Assert.Equal(HttpStatusCode.NoContent, signInResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task AdministratorWithCookie_IsAuthorizedForAdministrativeEndpoint()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var signInResponse = await client.PostAsync(
            $"/_test/sign-in/{XaquiamuniRoles.Administrador}",
            content: null);
        using var response = await client.GetAsync("/_test/admin");

        Assert.Equal(HttpStatusCode.NoContent, signInResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void CookieOptions_AreSecureHostOnlyAndNonSliding()
    {
        using var factory = CreateFactory();
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationConfiguration.CookieScheme);

        Assert.Equal(AuthenticationConfiguration.CookieName, options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = string.Empty
                }));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<CookieAuthenticationOptions>(
                    AuthenticationConfiguration.CookieScheme,
                    options => options.Events.OnValidatePrincipal = _ => Task.CompletedTask));
            builder.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/_test/authenticated", () => Results.Ok())
                        .RequireAuthorization();
                    endpoints.MapGet("/_test/admin", () => Results.Ok())
                        .RequireAuthorization(AuthorizationPolicies.Administracao);
                    endpoints.MapPost("/_test/sign-in/{role}", async (string role, HttpContext context) =>
                    {
                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, "test-user"),
                            new(ClaimTypes.Name, "test-user"),
                            new(ClaimTypes.Role, role)
                        };
                        var principal = new ClaimsPrincipal(new ClaimsIdentity(
                            claims,
                            AuthenticationConfiguration.CookieScheme));

                        await context.SignInAsync(AuthenticationConfiguration.CookieScheme, principal);
                        return Results.NoContent();
                    });
                });
            });
        });
}
