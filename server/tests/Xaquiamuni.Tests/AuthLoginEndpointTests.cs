using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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

public sealed class AuthLoginEndpointTests
{
    [Fact]
    public async Task PostLogin_WhenSuccessful_Returns204AndAuthCookie()
    {
        using var factory = CreateFactory(succeeded: true);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        using var request = CreateLoginRequest(new LoginClientRequest
        {
            Email = "cliente@exemplo.test",
            Senha = "SenhaSegura!123"
        }, csrf);

        using var response = await client.SendAsync(request);
        var authCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(AuthenticationConfiguration.CookieName + "=", authCookie);
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", authCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostLogin_WhenCredentialsAreRejected_ReturnsGeneric401WithoutAuthCookie()
    {
        using var factory = CreateFactory(succeeded: false);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        using var request = CreateLoginRequest(new LoginClientRequest
        {
            Email = "cliente@exemplo.test",
            Senha = "SenhaIncorreta!123"
        }, csrf);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Não foi possível autenticar", body, StringComparison.Ordinal);
        Assert.DoesNotContain("cliente@exemplo.test", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SenhaIncorreta!123", body, StringComparison.Ordinal);
        Assert.False(HasAuthCookie(response));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":\"cliente@exemplo.test\"}")]
    [InlineData("{\"senha\":\"SenhaSegura!123\"}")]
    [InlineData("{")]
    public async Task PostLogin_WithInvalidBody_Returns400WithoutAuthCookie(string body)
    {
        using var factory = CreateFactory(succeeded: true);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        AddCsrf(request, csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.False(HasAuthCookie(response));
    }

    [Fact]
    public async Task PostLogin_WithoutValidAntiforgery_IsRejectedWithoutAuthCookie()
    {
        using var factory = CreateFactory(succeeded: true);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(new LoginClientRequest
            {
                Email = "cliente@exemplo.test",
                Senha = "SenhaSegura!123"
            })
        };
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, "invalid-request-token");
        request.Headers.Add("Cookie", $"{AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.False(HasAuthCookie(response));
    }

    [Fact]
    public async Task PostLogin_UsesRequestTokenFromCsrfBodyNotCookieValue()
    {
        using var factory = CreateFactory(succeeded: true);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        Assert.NotEqual(csrf.CookieValue, csrf.RequestToken);

        using var request = CreateLoginRequest(new LoginClientRequest
        {
            Email = "cliente@exemplo.test",
            Senha = "SenhaSegura!123"
        }, csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains(AuthenticationConfiguration.CookieName + "=", Assert.Single(response.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public async Task PostLogin_WhenCsrfIsMissing_DoesNotCallLoginUseCase()
    {
        var fake = new FakeLoginCliente(succeeded: true);
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(new LoginClientRequest
            {
                Email = "cliente@exemplo.test",
                Senha = "SenhaSegura!123"
            })
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, fake.Calls);
        Assert.False(HasAuthCookie(response));
    }

    private static WebApplicationFactory<Program> CreateFactory(bool succeeded) =>
        CreateFactory(new FakeLoginCliente(succeeded));

    private static WebApplicationFactory<Program> CreateFactory(FakeLoginCliente fake) =>
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
                services.AddScoped<ILoginCliente>(provider =>
                {
                    fake.HttpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
                    return fake;
                });
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    private static HttpRequestMessage CreateLoginRequest(LoginClientRequest payload, CsrfState csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        AddCsrf(request, csrf);
        return request;
    }

    private static void AddCsrf(HttpRequestMessage request, CsrfState csrf)
    {
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        request.Headers.Add("Cookie", $"{AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");
    }

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestToken = Assert.IsType<string>(
            document.RootElement.GetProperty("requestToken").GetString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var cookieValue = cookie
            .Split(';', 2)[0]
            .Split('=', 2)[1];

        return new CsrfState(requestToken, cookieValue);
    }

    private static bool HasAuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(cookie => cookie.StartsWith(
            AuthenticationConfiguration.CookieName + "=",
            StringComparison.Ordinal));

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private sealed record CsrfState(string RequestToken, string CookieValue);

    private sealed class FakeLoginCliente(bool succeeded) : ILoginCliente
    {
        public IHttpContextAccessor? HttpContextAccessor { get; set; }
        public int Calls { get; private set; }

        public async Task<LoginClienteResult> ExecuteAsync(
            LoginClienteInput? input,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (!succeeded)
            {
                return LoginClienteResult.Failure();
            }

            var context = HttpContextAccessor?.HttpContext
                ?? throw new InvalidOperationException("Test HTTP context is unavailable.");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "test-client"),
                    new Claim(ClaimTypes.Name, "test-client"),
                    new Claim(ClaimTypes.Role, XaquiamuniRoles.Cliente)
                ],
                AuthenticationConfiguration.CookieScheme));
            await context.SignInAsync(AuthenticationConfiguration.CookieScheme, principal);
            return LoginClienteResult.Success();
        }
    }
}
