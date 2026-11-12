using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class SecurityStampCookieIntegrationTests
{
    private const string TestPassword = "Security Stamp Test 987!";

    [SqlServerIntegrationFact]
    public async Task ChangedSecurityStampRejectsOldCookieAndAllowsANewLogin()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var (userId, email) = await CreateUserAsync(factory);

        using var loginResponse = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
        AssertContainsSecurityStamp(factory, loginResponse, userId);

        using (var initialMe = await client.GetAsync(AuthEndpoints.MeEndpoint))
        {
            Assert.Equal(HttpStatusCode.OK, initialMe.StatusCode);
        }

        await UpdateSecurityStampAsync(factory, userId);

        using (var staleSession = await client.GetAsync(AuthEndpoints.MeEndpoint))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, staleSession.StatusCode);
            AssertNoStore(staleSession);
            Assert.Null(staleSession.Headers.Location);
        }

        using var newLoginResponse = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.NoContent, newLoginResponse.StatusCode);
        AssertContainsSecurityStamp(factory, newLoginResponse, userId);

        using var newSession = await client.GetAsync(AuthEndpoints.MeEndpoint);
        Assert.Equal(HttpStatusCode.OK, newSession.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task DeletedUserCannotContinueUsingAnOldCookie()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var (userId, email) = await CreateUserAsync(factory);

        using var loginResponse = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

        await DeleteUserAsync(factory, userId);

        using var response = await client.GetAsync(AuthEndpoints.MeEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
    }

    [SqlServerIntegrationFact]
    public async Task StampValidationDoesNotExtendTheOriginalEightHourCookieExpiry()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database, clock);
        using var client = CreateClient(factory);
        var (userId, email) = await CreateUserAsync(factory);

        using var loginResponse = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
        AssertContainsSecurityStamp(factory, loginResponse, userId);
        var initialTicket = GetTicket(factory, loginResponse);
        var initialExpiry = Assert.IsType<DateTimeOffset>(initialTicket.Properties.ExpiresUtc);
        var initialIssued = Assert.IsType<DateTimeOffset>(initialTicket.Properties.IssuedUtc);
        Assert.Equal(TimeSpan.FromHours(8), initialExpiry - initialIssued);

        clock.Advance(TimeSpan.FromHours(7));
        using (var refreshedSession = await client.GetAsync(AuthEndpoints.MeEndpoint))
        {
            Assert.Equal(HttpStatusCode.OK, refreshedSession.StatusCode);
            AssertNoStore(refreshedSession);
            var refreshedTicket = GetTicket(factory, refreshedSession, initialTicket);
            Assert.Equal(initialExpiry, refreshedTicket.Properties.ExpiresUtc);
            Assert.Equal(initialIssued, refreshedTicket.Properties.IssuedUtc);
        }

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromTicks(1));
        using var expiredSession = await client.GetAsync(AuthEndpoints.MeEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, expiredSession.StatusCode);
        AssertNoStore(expiredSession);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        SqlServerTestDatabase database,
        AdjustableTimeProvider? clock = null)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString,
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<XaquiamuniDbContext>(
                    options => options.UseSqlServer(connectionString));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                if (clock is not null)
                {
                    services.PostConfigure<CookieAuthenticationOptions>(
                        AuthenticationConfiguration.CookieScheme,
                        options => options.TimeProvider = clock);
                    services.PostConfigure<SecurityStampValidatorOptions>(
                        options => options.TimeProvider = clock);
                }
            });
        });

        using var scope = factory.Services.CreateScope();
        var configuredConnectionString = scope.ServiceProvider
            .GetRequiredService<XaquiamuniDbContext>()
            .Database.GetConnectionString();
        var expectedConnection = new SqlConnectionStringBuilder(connectionString);
        var configuredConnection = new SqlConnectionStringBuilder(configuredConnectionString);
        if (!string.Equals(
                configuredConnection.DataSource,
                @".\SQLEXPRESS",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                configuredConnection.InitialCatalog,
                expectedConnection.InitialCatalog,
                StringComparison.Ordinal)
            || !configuredConnection.InitialCatalog.StartsWith(
                "Xaquiamuni_IntegrationTest_",
                StringComparison.Ordinal))
        {
            factory.Dispose();
            throw new InvalidOperationException(
                "The HTTP test host is not configured for its isolated local SQL test database.");
        }

        return factory;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<(Guid UserId, string Email)> CreateUserAsync(
        WebApplicationFactory<Program> factory)
    {
        var email = $"stamp-{Guid.NewGuid():N}@example.test";
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = new XaquiamuniUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user, TestPassword);
        Assert.True(createResult.Succeeded, string.Join(',', createResult.Errors.Select(error => error.Code)));

        var roleResult = await userManager.AddToRoleAsync(user, XaquiamuniRoles.Cliente);
        Assert.True(roleResult.Succeeded, string.Join(',', roleResult.Errors.Select(error => error.Code)));
        return (user.Id, email);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email)
    {
        using var tokenResponse = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        var requestToken = Assert.IsType<string>(token?.RequestToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(new LoginClientRequest
            {
                Email = email,
                Senha = TestPassword
            })
        };
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);

        var response = await client.SendAsync(request);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                AuthenticationConfiguration.CookieName + "=",
                StringComparison.Ordinal));
        return response;
    }

    private static async Task UpdateSecurityStampAsync(
        WebApplicationFactory<Program> factory,
        Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var result = await userManager.UpdateSecurityStampAsync(user);
        Assert.True(result.Succeeded);
    }

    private static async Task DeleteUserAsync(
        WebApplicationFactory<Program> factory,
        Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var result = await userManager.DeleteAsync(user);
        Assert.True(result.Succeeded);
    }

    private static void AssertContainsSecurityStamp(
        WebApplicationFactory<Program> factory,
        HttpResponseMessage response,
        Guid userId)
    {
        var ticket = GetTicket(factory, response);
        var identityOptions = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(
            userId.ToString(),
            ticket.Principal.FindFirstValue(identityOptions.ClaimsIdentity.UserIdClaimType));
        Assert.False(string.IsNullOrWhiteSpace(
            ticket.Principal.FindFirstValue(identityOptions.ClaimsIdentity.SecurityStampClaimType)));
    }

    private static Microsoft.AspNetCore.Authentication.AuthenticationTicket GetTicket(
        WebApplicationFactory<Program> factory,
        HttpResponseMessage response,
        Microsoft.AspNetCore.Authentication.AuthenticationTicket? fallback = null)
    {
        var authCookie = response.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? setCookies.FirstOrDefault(value => value.StartsWith(
                AuthenticationConfiguration.CookieName + "=",
                StringComparison.Ordinal))
            : null;
        if (authCookie is null)
        {
            return Assert.IsType<Microsoft.AspNetCore.Authentication.AuthenticationTicket>(fallback);
        }

        var protectedTicket = authCookie.Split(';', 2)[0]
            .Split('=', 2)[1];
        var ticketDataFormat = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationConfiguration.CookieScheme)
            .TicketDataFormat;
        return Assert.IsType<Microsoft.AspNetCore.Authentication.AuthenticationTicket>(
            ticketDataFormat.Unprotect(protectedTicket));
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);

    private sealed record AntiforgeryResponse(string RequestToken);

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
