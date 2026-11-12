using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

public sealed class AuthMeEndpointTests
{
    private static readonly Guid ClientUserId = Guid.Parse("47c5be67-50f0-4777-a9b9-b989e3e1a158");

    [Fact]
    public async Task GetMe_AsAuthenticatedClient_ReturnsOnlyItsUserId()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);
        await SignInThroughLoginEndpoint(client);

        using var response = await client.GetAsync(AuthEndpoints.MeEndpoint);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var properties = document.RootElement.EnumerateObject().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        Assert.Equal(["userId"], properties.Select(property => property.Name).ToArray());
        Assert.Equal(ClientUserId, properties[0].Value.GetGuid());
    }

    [Fact]
    public async Task GetMe_WithoutAuthentication_Returns401WithoutRedirect()
    {
        using var factory = CreateFactory(XaquiamuniRoles.Cliente);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(AuthEndpoints.MeEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Content.Headers.ContentType?.MediaType?
            .Contains("html", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [Theory]
    [InlineData(XaquiamuniRoles.Administrador)]
    [InlineData(XaquiamuniRoles.Funcionario)]
    public async Task GetMe_AsNonClientRole_Returns403WithoutRedirect(string role)
    {
        using var factory = CreateFactory(role);
        using var client = CreateClient(factory);
        await SignInThroughLoginEndpoint(client);

        using var response = await client.GetAsync(AuthEndpoints.MeEndpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoStore(response);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Content.Headers.ContentType?.MediaType?
            .Contains("html", StringComparison.OrdinalIgnoreCase) ?? false);
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
                services.PostConfigure<CookieAuthenticationOptions>(
                    AuthenticationConfiguration.CookieScheme,
                    options => options.Events.OnValidatePrincipal = _ => Task.CompletedTask);
                services.RemoveAll<ILoginCliente>();
                services.AddScoped<ILoginCliente>(provider =>
                {
                    var fake = new FakeLoginCliente(role, ClientUserId)
                    {
                        HttpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>()
                    };
                    return fake;
                });
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task SignInThroughLoginEndpoint(HttpClient client)
    {
        using var csrfResponse = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfBody = await csrfResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var requestToken = Assert.IsType<string>(
            csrfBody?.RootElement.GetProperty("requestToken").GetString());
        Assert.Single(csrfResponse.Headers.GetValues("Set-Cookie"));

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(new LoginClientRequest
            {
                Email = "test-client@example.test",
                Senha = "TestOnly!Password123"
            })
        };
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(AuthenticationConfiguration.CookieName + "=", StringComparison.Ordinal));
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private sealed class FakeLoginCliente(string role, Guid userId) : ILoginCliente
    {
        public IHttpContextAccessor? HttpContextAccessor { get; set; }

        public async Task<LoginClienteResult> ExecuteAsync(
            LoginClienteInput? input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = HttpContextAccessor?.HttpContext
                ?? throw new InvalidOperationException("Test HTTP context is unavailable.");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, "test-account"),
                    new Claim(ClaimTypes.Email, "private@example.test"),
                    new Claim(ClaimTypes.Role, role)
                ],
                AuthenticationConfiguration.CookieScheme));
            await context.SignInAsync(AuthenticationConfiguration.CookieScheme, principal);
            return LoginClienteResult.Success();
        }
    }
}
