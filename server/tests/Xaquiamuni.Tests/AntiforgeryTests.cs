using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xaquiamuni.Api.Autenticacao;

namespace Xaquiamuni.Tests;

public class AntiforgeryTests
{
    [Fact]
    public async Task GetCsrf_ReturnsRequestTokenAndNonCacheableReadableCookie_HealthRemainsPublic()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var healthResponse = await client.GetAsync("/health");
        using var csrfResponse = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        var payload = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        var xsrfCookie = Assert.Single(csrfResponse.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        Assert.Contains("no-store", csrfResponse.Headers.CacheControl?.ToString());
        Assert.NotNull(payload?.RequestToken);
        Assert.Contains($"{AntiforgeryConfiguration.CookieName}=", xsrfCookie);
        Assert.Contains("secure", xsrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", xsrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", xsrfCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedPost_WithoutRequestToken_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        _ = await GetRequestToken(client);

        using var response = await client.PostAsync("/_test/protected", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedPost_WithInvalidRequestToken_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        _ = await GetRequestToken(client);
        client.DefaultRequestHeaders.Add(AntiforgeryConfiguration.HeaderName, "invalid-token");

        using var response = await client.PostAsync("/_test/protected", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedPost_WithValidCookieAndHeader_IsAccepted()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        client.DefaultRequestHeaders.Add(AntiforgeryConfiguration.HeaderName, requestToken);

        using var response = await client.PostAsync("/_test/protected", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
            builder.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
                    endpoints.MapXaquiamuniCsrfEndpoint();
                    endpoints.MapPost("/_test/protected", () => Results.Ok())
                        .RequireXaquiamuniAntiforgery();
                });
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<string> GetRequestToken(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        return Assert.IsType<string>(payload?.RequestToken);
    }

    private sealed record CsrfTokenResponse(string? RequestToken);
}
