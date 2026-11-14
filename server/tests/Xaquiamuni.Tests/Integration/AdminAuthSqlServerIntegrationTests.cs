using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
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
using Xaquiamuni.Admin;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class AdminAuthSqlServerIntegrationTests
{
    private const string TestPassword = "Admin Test Password 987!";

    [SqlServerIntegrationFact]
    public async Task ProvisionedAdministrator_CanLoginWithoutClientEmailGate_AndSecurityStampInvalidatesSession()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var (userId, email) = await ProvisionAdministratorAsync(factory);

        using var loginResponse = await LoginAdminAsync(client, email, TestPassword);
        var authCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
        AssertNoStore(loginResponse);
        Assert.Contains(AuthenticationConfiguration.CookieName + "=", authCookie);
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", authCookie, StringComparison.OrdinalIgnoreCase);

        using (var sessionResponse = await client.GetAsync(AdminAuthEndpoints.MeEndpoint))
        {
            Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
            AssertNoStore(sessionResponse);
            var session = await sessionResponse.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.Equal(userId, session?.RootElement.GetProperty("userId").GetGuid());
        }

        await UpdateSecurityStampAsync(factory, userId);

        using var invalidatedSession = await client.GetAsync(AdminAuthEndpoints.MeEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidatedSession.StatusCode);
        AssertNoStore(invalidatedSession);
    }

    [SqlServerIntegrationFact]
    public async Task AdminLogin_RejectsInvalidCredentialsAndNonAdminRolesWithoutIssuingCookie()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        await EnsureRoleAsync(factory, XaquiamuniRoles.Administrador);
        await EnsureRoleAsync(factory, XaquiamuniRoles.Funcionario);
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.test";
        var clientEmail = $"client-{Guid.NewGuid():N}@example.test";
        var employeeEmail = $"employee-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(factory, adminEmail, XaquiamuniRoles.Administrador, emailConfirmed: true);
        await CreateUserAsync(factory, clientEmail, XaquiamuniRoles.Cliente, emailConfirmed: true);
        await CreateUserAsync(factory, employeeEmail, XaquiamuniRoles.Funcionario, emailConfirmed: true);

        foreach (var (email, password) in new[]
        {
            (adminEmail, "WrongPassword!123"),
            (clientEmail, TestPassword),
            (employeeEmail, TestPassword)
        })
        {
            using var client = CreateClient(factory);
            using var response = await LoginAdminAsync(client, email, password);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            AssertNoStore(response);
            Assert.Contains(
                "Não foi possível autenticar com os dados informados.",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            Assert.False(HasAuthCookie(response));
        }
    }

    [SqlServerIntegrationFact]
    public async Task AdminEndpoints_RejectAnonymousAndClientSessions_AndLoginRequiresAntiforgery()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        var email = $"client-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(factory, email, XaquiamuniRoles.Cliente, emailConfirmed: true);

        using (var anonymousClient = CreateClient(factory))
        using (var anonymousSession = await anonymousClient.GetAsync(AdminAuthEndpoints.MeEndpoint))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousSession.StatusCode);
            AssertNoStore(anonymousSession);
        }

        using (var client = CreateClient(factory))
        {
            using var loginResponse = await LoginClientAsync(client, email, TestPassword);
            Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

            using var forbiddenSession = await client.GetAsync(AdminAuthEndpoints.MeEndpoint);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenSession.StatusCode);
            AssertNoStore(forbiddenSession);

            using var request = new HttpRequestMessage(HttpMethod.Post, AdminAuthEndpoints.LoginEndpoint)
            {
                Content = JsonContent.Create(new AdminLoginRequest
                {
                    Email = "admin@example.test",
                    Senha = TestPassword
                })
            };
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, "invalid-request-token");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
            Assert.False(HasAuthCookie(response));
        }
    }

    [SqlServerIntegrationFact]
    public async Task AdminLogout_RequiresAdminSessionAndExpiresSharedAuthenticationCookie()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var (_, email) = await ProvisionAdministratorAsync(factory);
        using var loginResponse = await LoginAdminAsync(client, email, TestPassword);
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, AdminAuthEndpoints.LogoutEndpoint);
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        using var response = await client.SendAsync(request);

        var expiredCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains(AuthenticationConfiguration.CookieName + "=", expiredCookie);
        Assert.Contains("expires=", expiredCookie, StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerIntegrationFact]
    public async Task ClientEmailConfirmationGate_RemainsRequiredAfterAddingAdminLogin()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var email = $"unconfirmed-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(factory, email, XaquiamuniRoles.Cliente, emailConfirmed: false);

        using var response = await LoginClientAsync(client, email, TestPassword);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoStore(response);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("EMAIL_NOT_CONFIRMED", document.RootElement.GetProperty("code").GetString());
        Assert.False(HasAuthCookie(response));
    }

    [SqlServerIntegrationFact]
    public async Task AdminAccountProvisioner_DoesNotCreateUserIfAdminRoleIsUnavailable()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var scope = database.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var provisioner = new AdminAccountProvisioner(userManager, roleManager, dbContext);

        var result = await provisioner.ProvisionAsync(email, TestPassword);

        Assert.Equal(AdminAccountProvisioningStatus.RoleAssignmentFailed, result);
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    private static WebApplicationFactory<Program> CreateFactory(SqlServerTestDatabase database)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<(Guid UserId, string Email)> ProvisionAdministratorAsync(
        WebApplicationFactory<Program> factory)
    {
        await EnsureRoleAsync(factory, XaquiamuniRoles.Administrador);
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var result = await new AdminAccountProvisioner(userManager, roleManager, dbContext)
            .ProvisionAsync(email, TestPassword);

        Assert.Equal(AdminAccountProvisioningStatus.Created, result);
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        return (user.Id, email);
    }

    private static async Task EnsureRoleAsync(
        WebApplicationFactory<Program> factory,
        string roleName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        Assert.True(result.Succeeded, string.Join(',', result.Errors.Select(error => error.Code)));
    }

    private static async Task CreateUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        string role,
        bool emailConfirmed)
    {
        await EnsureRoleAsync(factory, role);
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = new XaquiamuniUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = emailConfirmed
        };
        var createResult = await userManager.CreateAsync(user, TestPassword);
        Assert.True(createResult.Succeeded, string.Join(',', createResult.Errors.Select(error => error.Code)));

        var roleResult = await userManager.AddToRoleAsync(user, role);
        Assert.True(roleResult.Succeeded, string.Join(',', roleResult.Errors.Select(error => error.Code)));
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
        Assert.True(result.Succeeded, string.Join(',', result.Errors.Select(error => error.Code)));
    }

    private static async Task<HttpResponseMessage> LoginAdminAsync(
        HttpClient client,
        string email,
        string password) =>
        await LoginAsync(
            client,
            AdminAuthEndpoints.LoginEndpoint,
            new AdminLoginRequest { Email = email, Senha = password });

    private static async Task<HttpResponseMessage> LoginClientAsync(
        HttpClient client,
        string email,
        string password) =>
        await LoginAsync(
            client,
            AuthEndpoints.LoginEndpoint,
            new LoginClientRequest { Email = email, Senha = password });

    private static async Task<HttpResponseMessage> LoginAsync<TRequest>(
        HttpClient client,
        string endpoint,
        TRequest requestBody)
    {
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        return await client.SendAsync(request);
    }

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var requestToken = Assert.IsType<string>(body?.RootElement.GetProperty("requestToken").GetString());
        return new CsrfState(requestToken);
    }

    private static bool HasAuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(cookie => cookie.StartsWith(
            AuthenticationConfiguration.CookieName + "=",
            StringComparison.Ordinal));

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private sealed record CsrfState(string RequestToken);
}
