using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class AuthPasswordRecoveryRateLimitingTests
{
    private static readonly IPAddress FirstIp = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress SecondIp = IPAddress.Parse("203.0.113.11");
    private const string TestEmail = "client@example.invalid";
    private const string TestToken = "test-password-reset-token";
    private const string TestPassword = "Valid Password 987!";
    private const string RateLimitMessage =
        "Muitas solicitações. Tente novamente mais tarde.";

    [Fact]
    public async Task ForgotPassword_TenthRequestIsAdmittedAndEleventhIsThrottled()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendForgotPasswordAsync(client, tokens);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            AssertNoStore(response);
        }

        var rejected = await SendForgotPasswordAsync(client, tokens);

        AssertRateLimitResponse(rejected);
        Assert.Equal(10, useCase.Calls);
    }

    [Fact]
    public async Task ResetPassword_TenthRequestIsAdmittedAndEleventhIsThrottled()
    {
        var service = new FakePasswordResetService();
        using var factory = CreateFactory(passwordResetService: service);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendResetPasswordAsync(client, tokens);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        var rejected = await SendResetPasswordAsync(client, tokens);

        AssertRateLimitResponse(rejected);
        Assert.Equal(10, service.ResetCalls);
    }

    [Fact]
    public async Task RecoveryPoliciesAndConfirmationResendHaveIndependentCounters()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        var passwordResetService = new FakePasswordResetService();
        var resendUseCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase, passwordResetService, resendUseCase);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);
        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            HttpStatusCode.BadRequest);
        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ResendConfirmationEmailEndpoint,
            ResendBody,
            HttpStatusCode.Accepted);

        Assert.Equal(10, useCase.Calls);
        Assert.Equal(10, passwordResetService.ResetCalls);
        Assert.Equal(10, resendUseCase.Calls);

        using var secondClient = CreateClient(factory, SecondIp);
        var secondTokens = await GetAntiforgeryTokens(secondClient);
        await AssertAdmittedAsync(
            secondClient,
            secondTokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);
        await AssertAdmittedAsync(
            secondClient,
            secondTokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExhaustingResendConfirmationDoesNotBlockPasswordRecoveryOrReset()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        var passwordResetService = new FakePasswordResetService();
        var resendUseCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(useCase, passwordResetService, resendUseCase);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ResendConfirmationEmailEndpoint,
            ResendBody,
            HttpStatusCode.Accepted);

        await AssertAdmittedAsync(
            client,
            tokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);
        await AssertAdmittedAsync(
            client,
            tokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            HttpStatusCode.BadRequest);
        Assert.Equal(1, useCase.Calls);
        Assert.Equal(1, passwordResetService.ResetCalls);
    }

    [Fact]
    public async Task ForgotAndResetPoliciesPartitionIndependentlyByRemoteIp()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        var passwordResetService = new FakePasswordResetService();
        using var factory = CreateFactory(useCase, passwordResetService);
        using var firstClient = CreateClient(factory, FirstIp);
        using var secondClient = CreateClient(factory, SecondIp);
        var firstTokens = await GetAntiforgeryTokens(firstClient);
        var secondTokens = await GetAntiforgeryTokens(secondClient);

        await AssertExhaustedAsync(
            firstClient,
            firstTokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);
        await AssertAdmittedAsync(
            secondClient,
            secondTokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);

        await AssertExhaustedAsync(
            firstClient,
            firstTokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            HttpStatusCode.BadRequest);
        await AssertAdmittedAsync(
            secondClient,
            secondTokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgedForwardedHeadersCannotBypassEitherRecoveryPolicy()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        var passwordResetService = new FakePasswordResetService();
        using var factory = CreateFactory(useCase, passwordResetService);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);
        var rejectedForgot = await SendAsync(
            client,
            tokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            forwardedFor: "198.51.100.77",
            realIp: "198.51.100.77");
        AssertRateLimitResponse(rejectedForgot);

        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            HttpStatusCode.BadRequest);
        var rejectedReset = await SendAsync(
            client,
            tokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            forwardedFor: "198.51.100.77",
            realIp: "198.51.100.77");
        AssertRateLimitResponse(rejectedReset);
    }

    [Fact]
    public async Task InvalidEmailTokenAndPasswordRequestsConsumeTheirPolicyLimits()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        var passwordResetService = new FakePasswordResetService(
            (_, _, password) => password == "weak"
                ? PasswordResetResult.InvalidPassword()
                : PasswordResetResult.InvalidRequest());
        using var factory = CreateFactory(useCase, passwordResetService);
        using var forgotClient = CreateClient(factory, FirstIp);
        using var invalidTokenClient = CreateClient(factory, SecondIp);
        using var invalidPasswordClient = CreateClient(
            factory,
            IPAddress.Parse("203.0.113.12"));
        var forgotTokens = await GetAntiforgeryTokens(forgotClient);
        var invalidTokenTokens = await GetAntiforgeryTokens(invalidTokenClient);
        var invalidPasswordTokens = await GetAntiforgeryTokens(invalidPasswordClient);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendForgotPasswordAsync(
                forgotClient,
                forgotTokens,
                email: "invalid-email");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        AssertRateLimitResponse(await SendForgotPasswordAsync(forgotClient, forgotTokens));
        Assert.Equal(10, useCase.Calls);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendResetPasswordAsync(
                invalidTokenClient,
                invalidTokenTokens,
                token: "invalid-token");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        AssertRateLimitResponse(await SendResetPasswordAsync(invalidTokenClient, invalidTokenTokens));
        Assert.Equal(10, passwordResetService.ResetCalls);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendResetPasswordAsync(
                invalidPasswordClient,
                invalidPasswordTokens,
                newPassword: "weak");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        AssertRateLimitResponse(await SendResetPasswordAsync(
            invalidPasswordClient,
            invalidPasswordTokens));
        Assert.Equal(20, passwordResetService.ResetCalls);
    }

    [Fact]
    public async Task MalformedBodyConsumesLimitBeforeEndpointBinding()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendAsync(
                client,
                tokens,
                AuthEndpoints.ForgotPasswordEndpoint,
                "{");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        AssertRateLimitResponse(await SendForgotPasswordAsync(client, tokens));
        Assert.Equal(0, useCase.Calls);
    }

    [Fact]
    public async Task AntiforgeryRemainsRequiredAndInvalidRequestsConsumeLimitBeforeFilter()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        for (var index = 0; index < 10; index++)
        {
            var response = await SendAsync(
                client,
                tokens,
                AuthEndpoints.ForgotPasswordEndpoint,
                ForgotBody,
                requestToken: "invalid-token");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }

        AssertRateLimitResponse(await SendForgotPasswordAsync(client, tokens));
        Assert.Equal(0, useCase.Calls);
    }

    [Fact]
    public async Task ConcurrentRequestsAdmitExactlyTenForEachRecoveryPolicy()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        var passwordResetService = new FakePasswordResetService();
        using var factory = CreateFactory(useCase, passwordResetService);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        var forgotResponses = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => SendForgotPasswordAsync(client, tokens)));
        Assert.Equal(10, forgotResponses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(10, forgotResponses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.All(
            forgotResponses.Where(response => response.StatusCode == HttpStatusCode.TooManyRequests),
            AssertRateLimitResponse);
        Assert.Equal(10, useCase.Calls);

        var resetResponses = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => SendResetPasswordAsync(client, tokens)));
        Assert.Equal(10, resetResponses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(10, resetResponses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.All(
            resetResponses.Where(response => response.StatusCode == HttpStatusCode.TooManyRequests),
            AssertRateLimitResponse);
        Assert.Equal(10, passwordResetService.ResetCalls);
    }

    [Fact]
    public async Task NewPoliciesNormalizeMappedIpv4AndUseSharedFallbackForUnknownIp()
    {
        var useCase = new FakePasswordRecoveryUseCase();
        var passwordResetService = new FakePasswordResetService();
        using var factory = CreateFactory(useCase, passwordResetService);
        using var ipv4Client = CreateClient(factory, FirstIp);
        var ipv4Tokens = await GetAntiforgeryTokens(ipv4Client);

        await AssertExhaustedAsync(
            ipv4Client,
            ipv4Tokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);
        using var mappedClient = CreateClient(factory, FirstIp.MapToIPv6());
        var mappedTokens = await GetAntiforgeryTokens(mappedClient);
        AssertRateLimitResponse(await SendForgotPasswordAsync(mappedClient, mappedTokens));

        using var unknownIpClient = CreateClient(factory, remoteIpAddress: null);
        var unknownIpTokens = await GetAntiforgeryTokens(unknownIpClient);
        for (var index = 0; index < 10; index++)
        {
            var response = await SendAsync(
                unknownIpClient,
                unknownIpTokens,
                AuthEndpoints.ResetPasswordEndpoint,
                ResetBody,
                forwardedFor: $"198.51.100.{index + 1}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        AssertRateLimitResponse(await SendAsync(
            unknownIpClient,
            unknownIpTokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            forwardedFor: "192.0.2.200",
            realIp: "192.0.2.200"));
    }

    [Fact]
    public async Task NonRecoveryRoutesRemainOutsideTheNewPolicies()
    {
        var resendUseCase = new FakeResendConfirmationUseCase();
        using var factory = CreateFactory(resendUseCase: resendUseCase);
        using var client = CreateClient(factory, FirstIp);
        var tokens = await GetAntiforgeryTokens(client);

        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            ForgotBody,
            HttpStatusCode.Accepted);
        await AssertExhaustedAsync(
            client,
            tokens,
            AuthEndpoints.ResetPasswordEndpoint,
            ResetBody,
            HttpStatusCode.BadRequest);

        var resend = await SendAsync(
            client,
            tokens,
            AuthEndpoints.ResendConfirmationEmailEndpoint,
            ResendBody);
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);

        foreach (var endpoint in new[]
                 {
                     AuthEndpoints.RegisterEndpoint,
                     AuthEndpoints.LoginEndpoint,
                     AuthEndpoints.ConfirmEmailEndpoint
                 })
        {
            var response = await SendAsync(client, tokens, endpoint, "{");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var currentClient = await SendGetAsync(client, AuthEndpoints.MeEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, currentClient.StatusCode);
        var logout = await SendAsync(client, tokens, AuthEndpoints.LogoutEndpoint, "{}");
        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
        var csrf = await SendGetAsync(client, AntiforgeryConfiguration.TokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Assert.Equal(1, resendUseCase.Calls);
    }

    private static readonly string ForgotBody = JsonSerializer.Serialize(new { email = TestEmail });
    private static readonly string ResetBody = JsonSerializer.Serialize(new
    {
        email = TestEmail,
        token = TestToken,
        newPassword = TestPassword
    });
    private static readonly string ResendBody = JsonSerializer.Serialize(new { email = TestEmail });

    private static WebApplicationFactory<Program> CreateFactory(
        FakePasswordRecoveryUseCase? useCase = null,
        FakePasswordResetService? passwordResetService = null,
        FakeResendConfirmationUseCase? resendUseCase = null) =>
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
                services.RemoveAll<ISolicitarRecuperacaoSenha>();
                services.AddSingleton<ISolicitarRecuperacaoSenha>(
                    useCase ?? new FakePasswordRecoveryUseCase());
                services.RemoveAll<IPasswordResetService>();
                services.AddSingleton<IPasswordResetService>(
                    passwordResetService ?? new FakePasswordResetService());
                services.RemoveAll<ISolicitarReenvioConfirmacaoEmail>();
                services.AddSingleton<ISolicitarReenvioConfirmacaoEmail>(
                    resendUseCase ?? new FakeResendConfirmationUseCase());
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
        return new AntiforgeryTokens(requestToken, setCookie.Split(';', 2)[0]);
    }

    private static Task<TestResponse> SendForgotPasswordAsync(
        HttpClient client,
        AntiforgeryTokens tokens,
        string? email = TestEmail) =>
        SendAsync(
            client,
            tokens,
            AuthEndpoints.ForgotPasswordEndpoint,
            JsonSerializer.Serialize(new { email }));

    private static Task<TestResponse> SendResetPasswordAsync(
        HttpClient client,
        AntiforgeryTokens tokens,
        string token = TestToken,
        string newPassword = TestPassword) =>
        SendAsync(
            client,
            tokens,
            AuthEndpoints.ResetPasswordEndpoint,
            JsonSerializer.Serialize(new
            {
                email = TestEmail,
                token,
                newPassword
            }));

    private static async Task<TestResponse> SendAsync(
        HttpClient client,
        AntiforgeryTokens tokens,
        string endpoint,
        string body,
        string? forwardedFor = null,
        string? realIp = null,
        string? requestToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", tokens.Cookie);
        if (requestToken is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);
        }
        else
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, tokens.RequestToken);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        if (realIp is not null)
        {
            request.Headers.Add("X-Real-IP", realIp);
        }

        using var response = await client.SendAsync(request);
        var headers = response.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value),
            StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        return new TestResponse(
            response.StatusCode,
            headers,
            await response.Content.ReadAsStringAsync());
    }

    private static async Task<TestResponse> SendGetAsync(HttpClient client, string endpoint)
    {
        using var response = await client.GetAsync(endpoint);
        var headers = response.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value),
            StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        return new TestResponse(
            response.StatusCode,
            headers,
            await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertExhaustedAsync(
        HttpClient client,
        AntiforgeryTokens tokens,
        string endpoint,
        string body,
        HttpStatusCode admittedStatus)
    {
        for (var index = 0; index < 10; index++)
        {
            var response = await SendAsync(client, tokens, endpoint, body);
            Assert.Equal(admittedStatus, response.StatusCode);
            AssertNoStore(response);
        }

        AssertRateLimitResponse(await SendAsync(client, tokens, endpoint, body));
    }

    private static async Task AssertAdmittedAsync(
        HttpClient client,
        AntiforgeryTokens tokens,
        string endpoint,
        string body,
        HttpStatusCode expectedStatus)
    {
        var response = await SendAsync(client, tokens, endpoint, body);
        Assert.Equal(expectedStatus, response.StatusCode);
        AssertNoStore(response);
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
        Assert.Equal(
            "application/json",
            response.Headers.GetValueOrDefault("Content-Type")?.Split(';', 2)[0]);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal(
            RateLimitMessage,
            document.RootElement.GetProperty("message").GetString());
        Assert.True(int.TryParse(
            response.Headers.GetValueOrDefault("Retry-After"),
            out var retryAfterSeconds));
        Assert.True(retryAfterSeconds > 0);
        Assert.DoesNotContain(TestEmail, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestToken, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPassword, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Outbox", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecurityStamp", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fingerprint", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.ContainsKey("Location"));
        Assert.False(response.Headers.ContainsKey("Set-Cookie"));
    }

    private sealed record AntiforgeryTokens(string RequestToken, string Cookie);

    private sealed record TestResponse(
        HttpStatusCode StatusCode,
        Dictionary<string, string> Headers,
        string Body);

    private sealed class FakePasswordRecoveryUseCase : ISolicitarRecuperacaoSenha
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<SolicitarRecuperacaoSenhaResult> ExecuteAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var result = string.IsNullOrWhiteSpace(email)
                || !email.Contains('@', StringComparison.Ordinal)
                ? SolicitarRecuperacaoSenhaResult.InvalidEmail()
                : SolicitarRecuperacaoSenhaResult.RequestProcessed();
            return Task.FromResult(result);
        }
    }

    private sealed class FakePasswordResetService : IPasswordResetService
    {
        private readonly Func<string?, string?, string?, PasswordResetResult> _resultFactory;
        private int _resetCalls;

        public FakePasswordResetService(
            Func<string?, string?, string?, PasswordResetResult>? resultFactory = null)
        {
            _resultFactory = resultFactory
                ?? ((_, _, _) => PasswordResetResult.InvalidRequest());
        }

        public int ResetCalls => Volatile.Read(ref _resetCalls);

        public Task<Guid?> FindConfirmedUserIdByEmailAsync(
            string? email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PasswordResetTokenResult> GeneratePasswordResetTokenForUserAsync(
            Guid userId,
            ReadOnlyMemory<byte>? credentialGenerationFingerprint,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PasswordResetResult> ResetPasswordAsync(
            string? email,
            string? token,
            string? newPassword,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _resetCalls);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_resultFactory(email, token, newPassword));
        }
    }

    private sealed class FakeResendConfirmationUseCase : ISolicitarReenvioConfirmacaoEmail
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<SolicitarReenvioConfirmacaoEmailResult> ExecuteAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(SolicitarReenvioConfirmacaoEmailResult.RequestProcessed());
        }
    }
}
