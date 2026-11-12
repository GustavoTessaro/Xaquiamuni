using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class AuthEmailConfirmationResendRateLimitingTests
{
    private static readonly IPAddress ClientIp = IPAddress.Parse("203.0.113.10");
    private const string TestEmail = "client@example.invalid";

    [Fact]
    public async Task PostResend_TenthRequestFromSameIpIsAcceptedAndEleventhIsThrottled()
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, ClientIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendResendAsync(client, tokens, TestEmail);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            AssertNoStore(response);
        }

        var rejected = await SendResendAsync(client, tokens, TestEmail);

        AssertRateLimitResponse(rejected);
        Assert.True(rejected.Headers.ContainsKey("Retry-After"));
        Assert.Equal(10, useCase.Calls);
        Assert.DoesNotContain(TestEmail, rejected.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostResend_DifferentIpHasAnIndependentQuota()
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var firstClient = CreateClient(factory, ClientIp);
        var tokens = await GetAntiforgeryTokens(firstClient);

        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await SendResendAsync(firstClient, tokens, TestEmail)).StatusCode);
        }

        using var secondClient = CreateClient(factory, IPAddress.Parse("203.0.113.11"));
        var secondIpResponse = await SendResendAsync(secondClient, tokens, TestEmail);

        Assert.Equal(HttpStatusCode.Accepted, secondIpResponse.StatusCode);
        AssertNoStore(secondIpResponse);
        Assert.Equal(11, useCase.Calls);
    }

    [Fact]
    public async Task PostResend_MappedIpv4AddressUsesTheSamePartitionAsIpv4()
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, ClientIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await SendResendAsync(client, tokens, TestEmail)).StatusCode);
        }

        using var mappedClient = CreateClient(factory, ClientIp.MapToIPv6());
        var response = await SendResendAsync(mappedClient, tokens, TestEmail);

        AssertRateLimitResponse(response);
        Assert.Equal(10, useCase.Calls);
    }

    [Fact]
    public async Task PostResend_MissingRemoteIpUsesOneSharedFallbackPartition()
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, remoteIpAddress: null);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendResendAsync(
                client,
                tokens,
                TestEmail,
                forwardedFor: $"198.51.100.{index + 1}");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var rejected = await SendResendAsync(
            client,
            tokens,
            TestEmail,
            forwardedFor: "192.0.2.200");

        AssertRateLimitResponse(rejected);
        Assert.Equal(10, useCase.Calls);
    }

    [Fact]
    public async Task PostResend_ArbitraryForwardedForValuesCannotBypassRemoteIpQuota()
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, ClientIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await SendResendAsync(
                    client,
                    tokens,
                    TestEmail,
                    forwardedFor: $"198.51.100.{index + 1}")).StatusCode);
        }

        var rejected = await SendResendAsync(
            client,
            tokens,
            TestEmail,
            forwardedFor: "192.0.2.200");

        AssertRateLimitResponse(rejected);
        Assert.Equal(10, useCase.Calls);
    }

    [Fact]
    public async Task PostResend_InvalidRequestsConsumeTheIpQuota()
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, ClientIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendResendAsync(client, tokens, "invalid-email");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        var rejected = await SendResendAsync(client, tokens, TestEmail);

        AssertRateLimitResponse(rejected);
        Assert.Equal(10, useCase.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostResend_AntiforgeryRemainsRequired(bool useInvalidToken)
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, ClientIp);
        var tokens = await GetAntiforgeryTokens(client);

        var response = await SendResendAsync(
            client,
            tokens,
            TestEmail,
            requestToken: useInvalidToken ? "invalid-token" : null,
            includeRequestToken: useInvalidToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, useCase.Calls);
    }

    [Fact]
    public async Task ResendRateLimit_DoesNotAffectRegistrationLoginOrConfirmationRoutes()
    {
        var useCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, ClientIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await SendResendAsync(client, tokens, TestEmail)).StatusCode);
        }

        foreach (var endpoint in new[]
                 {
                     AuthEndpoints.RegisterEndpoint,
                     AuthEndpoints.LoginEndpoint,
                     AuthEndpoints.ConfirmEmailEndpoint
                 })
        {
            var response = await SendAsync(client, endpoint, tokens, "{");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        Assert.Equal(10, useCase.Calls);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeResendConfirmationUseCase useCase) =>
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
                services.RemoveAll<ISolicitarReenvioConfirmacaoEmail>();
                services.AddSingleton<ISolicitarReenvioConfirmacaoEmail>(useCase);
            });
        });

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        IPAddress? remoteIpAddress) =>
        new(factory.Server.CreateHandler(context =>
        {
            context.Connection.RemoteIpAddress = remoteIpAddress;
        }))
        {
            BaseAddress = new Uri("https://localhost")
        };

    private static async Task<AntiforgeryTokens> GetAntiforgeryTokens(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestToken = Assert.IsType<string>(
            document.RootElement.GetProperty("requestToken").GetString());
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var antiforgeryCookie = setCookie.Split(';', 2)[0];
        return new AntiforgeryTokens(requestToken, antiforgeryCookie);
    }

    private static Task<TestResponse> SendResendAsync(
        HttpClient client,
        AntiforgeryTokens tokens,
        string? email,
        string? forwardedFor = null,
        string? requestToken = null,
        bool includeRequestToken = true) =>
        SendAsync(
            client,
            AuthEndpoints.ResendConfirmationEmailEndpoint,
            tokens,
            JsonSerializer.Serialize(new { email }),
            forwardedFor,
            includeRequestToken ? requestToken ?? tokens.RequestToken : null);

    private static async Task<TestResponse> SendAsync(
        HttpClient client,
        string path,
        AntiforgeryTokens? tokens,
        string body,
        string? forwardedFor = null,
        string? requestToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (tokens is not null)
        {
            request.Headers.Add("Cookie", tokens.Cookie);
        }

        if (requestToken is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        using var response = await client.SendAsync(request);
        var responseHeaders = response.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value),
            StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Content.Headers)
        {
            responseHeaders[header.Key] = string.Join(",", header.Value);
        }

        return new TestResponse(
            response.StatusCode,
            responseHeaders,
            await response.Content.ReadAsStringAsync());
    }

    private static void AssertNoStore(TestResponse response) =>
        Assert.Contains(
            "no-store",
            response.Headers.GetValueOrDefault("Cache-Control"),
            StringComparison.OrdinalIgnoreCase);

    private static void AssertRateLimitResponse(TestResponse response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("Muitas solicitações", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestEmail, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.10", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Outbox", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AntiforgeryTokens(string RequestToken, string Cookie);

    private sealed record TestResponse(
        HttpStatusCode StatusCode,
        Dictionary<string, string> Headers,
        string Body);

    private sealed class FakeResendConfirmationUseCase : ISolicitarReenvioConfirmacaoEmail
    {
        public int Calls { get; private set; }

        public Task<SolicitarReenvioConfirmacaoEmailResult> ExecuteAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(string.IsNullOrWhiteSpace(email)
                || !email.Contains('@', StringComparison.Ordinal)
                ? SolicitarReenvioConfirmacaoEmailResult.InvalidEmail()
                : SolicitarReenvioConfirmacaoEmailResult.RequestProcessed());
        }
    }
}
