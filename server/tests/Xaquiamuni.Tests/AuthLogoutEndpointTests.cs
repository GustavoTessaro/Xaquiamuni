using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class AuthLogoutEndpointTests
{
    private static readonly Guid ClientUserId = Guid.Parse("73ab10e9-9e45-48ca-83e3-e3eb24eab44e");

    [Fact]
    public async Task PostLogout_AsClientWithValidCsrf_Returns204AndExpiresBrowserCookie()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);
        var loginCsrf = await GetCsrfAsync(client);
        var authCookie = await SignInAndGetCookieAsync(client, loginCsrf);
        var csrf = await GetCsrfAsync(client, authCookie);

        using var response = await PostLogoutAsync(client, authCookie, csrf);
        var expiredCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains(AuthenticationConfiguration.CookieName + "=", expiredCookie);
        AssertCookieExpired(expiredCookie);
    }

    [Fact]
    public async Task PostLogout_DoesNotRevokePreviouslyCopiedStatelessCookie()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);
        var loginCsrf = await GetCsrfAsync(client);
        var copiedAuthCookie = await SignInAndGetCookieAsync(client, loginCsrf);
        var csrf = await GetCsrfAsync(client, copiedAuthCookie);

        using var logout = await PostLogoutAsync(client, copiedAuthCookie, csrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, AuthEndpoints.MeEndpoint);
        request.Headers.Add("Cookie", copiedAuthCookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(ClientUserId, body?.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task PostLogout_WithoutAuthentication_Returns401WithoutRedirect()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(AuthEndpoints.LogoutEndpoint, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        AssertNoHtml(response);
    }

    [Theory]
    [InlineData(XaquiamuniRoles.Administrador)]
    [InlineData(XaquiamuniRoles.Funcionario)]
    public async Task PostLogout_WithNonClientRole_Returns403WithoutRedirect(string role)
    {
        using var factory = CreateFactory(role);
        using var client = CreateClient(factory);
        var loginCsrf = await GetCsrfAsync(client);
        var authCookie = await SignInAndGetCookieAsync(client, loginCsrf);
        var csrf = await GetCsrfAsync(client, authCookie);

        using var response = await PostLogoutAsync(client, authCookie, csrf);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        AssertNoHtml(response);
        Assert.Empty(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Where(cookie => cookie.StartsWith(
                AuthenticationConfiguration.CookieName + "=",
                StringComparison.Ordinal))
            : []);
    }

    [Fact]
    public async Task PostLogout_WithoutCsrfToken_IsRejected()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);
        var loginCsrf = await GetCsrfAsync(client);
        var authCookie = await SignInAndGetCookieAsync(client, loginCsrf);
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LogoutEndpoint);
        request.Headers.Add("Cookie", authCookie);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.False(HasAuthCookie(response));
    }

    [Fact]
    public async Task PostLogout_WithInvalidCsrfToken_IsRejected()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);
        var loginCsrf = await GetCsrfAsync(client);
        var authCookie = await SignInAndGetCookieAsync(client, loginCsrf);
        var csrf = await GetCsrfAsync(client, authCookie);
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LogoutEndpoint);
        request.Headers.Add("Cookie", $"{authCookie}; {AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, "invalid-request-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.False(HasAuthCookie(response));
    }

    [Fact]
    public async Task PostLogout_UsesRequestTokenFromCsrfBodyDistinctFromCookie()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);
        var loginCsrf = await GetCsrfAsync(client);
        var authCookie = await SignInAndGetCookieAsync(client, loginCsrf);
        var csrf = await GetCsrfAsync(client, authCookie);

        Assert.NotEqual(csrf.RequestToken, csrf.CookieValue);
        using var response = await PostLogoutAsync(client, authCookie, csrf);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
    }

    private static WebApplicationFactory<Program> CreateFactory(string role) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = string.Empty
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILoginCliente>();
                services.AddScoped<ILoginCliente>(provider => new FakeLoginCliente(
                    role,
                    ClientUserId,
                    provider.GetRequiredService<IHttpContextAccessor>()));
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    private static async Task<string> SignInAndGetCookieAsync(HttpClient client, CsrfState csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(new LoginClientRequest
            {
                Email = "test-client@example.test",
                Senha = "TestOnly!Password123"
            })
        };
        request.Headers.Add("Cookie", $"{AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var authSetCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(AuthenticationConfiguration.CookieName + "=", authSetCookie, StringComparison.Ordinal);
        return authSetCookie.Split(';', 2)[0];
    }

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client, string? authCookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AntiforgeryConfiguration.TokenEndpoint);
        if (authCookie is not null)
        {
            request.Headers.Add("Cookie", authCookie);
        }

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var requestToken = Assert.IsType<string>(body?.RootElement.GetProperty("requestToken").GetString());
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var cookieValue = setCookie.Split(';', 2)[0].Split('=', 2)[1];
        return new CsrfState(requestToken, cookieValue);
    }

    private static Task<HttpResponseMessage> PostLogoutAsync(
        HttpClient client,
        string authCookie,
        CsrfState csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LogoutEndpoint);
        request.Headers.Add("Cookie", $"{authCookie}; {AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        return client.SendAsync(request);
    }

    private static void AssertCookieExpired(string setCookie)
    {
        var expires = setCookie.Split(';')
            .Select(part => part.Trim())
            .Single(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))
            .Split('=', 2)[1];
        Assert.True(DateTimeOffset.TryParse(
            expires,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var expiry));
        Assert.True(expiry <= DateTimeOffset.UtcNow);
    }

    private static bool HasAuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(cookie => cookie.StartsWith(
            AuthenticationConfiguration.CookieName + "=",
            StringComparison.Ordinal));

    private static void AssertNoHtml(HttpResponseMessage response) =>
        Assert.False(response.Content.Headers.ContentType?.MediaType?
            .Contains("html", StringComparison.OrdinalIgnoreCase) ?? false);

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private sealed record CsrfState(string RequestToken, string CookieValue);

    private sealed class FakeLoginCliente(
        string role,
        Guid userId,
        IHttpContextAccessor httpContextAccessor) : ILoginCliente
    {
        public async Task<LoginClienteResult> ExecuteAsync(
            LoginClienteInput? input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("Test HTTP context is unavailable.");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, "test-account"),
                    new Claim(ClaimTypes.Role, role)
                ],
                AuthenticationConfiguration.CookieScheme));
            await context.SignInAsync(AuthenticationConfiguration.CookieScheme, principal);
            return LoginClienteResult.Success();
        }
    }
}
