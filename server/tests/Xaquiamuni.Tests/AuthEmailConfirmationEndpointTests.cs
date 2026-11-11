using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Api.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class AuthEmailConfirmationEndpointTests
{
    private const string TestEmail = "client@example.invalid";
    private const string TestToken = "test-only-confirmation-token";

    [Fact]
    public async Task PostConfirmEmail_WhenSuccessful_ReturnsMinimalNoContent()
    {
        var service = new FakeEmailConfirmationService(EmailConfirmationResult.Confirmed());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetRequestToken(client);

        using var request = CreateRequest(TestEmail, TestToken, antiforgeryToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, service.Calls);
        Assert.Equal(TestEmail, service.LastEmail);
        Assert.Equal(TestToken, service.LastToken);
    }

    [Theory]
    [InlineData(EmailConfirmationStatus.InvalidOrExpiredToken)]
    [InlineData(EmailConfirmationStatus.AlreadyConfirmed)]
    [InlineData(EmailConfirmationStatus.UserNotFound)]
    public async Task PostConfirmEmail_WhenConfirmationCannotBeCompleted_ReturnsSameGenericResponse(
        EmailConfirmationStatus status)
    {
        var service = new FakeEmailConfirmationService(ToResult(status));
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetRequestToken(client);

        using var request = CreateRequest(TestEmail, TestToken, antiforgeryToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("Não foi possível confirmar o e-mail.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestEmail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestToken, body, StringComparison.Ordinal);
        Assert.Equal(1, service.Calls);
    }

    [Theory]
    [InlineData(null, TestToken)]
    [InlineData("", TestToken)]
    [InlineData("invalid-email", TestToken)]
    [InlineData("client@example.invalid", null)]
    [InlineData("client@example.invalid", "")]
    public async Task PostConfirmEmail_WithMissingOrInvalidFields_ReturnsValidationProblem(
        string? email,
        string? token)
    {
        var service = new FakeEmailConfirmationService(EmailConfirmationResult.Confirmed());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetRequestToken(client);

        using var request = CreateRequest(email, token, antiforgeryToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("Informe um e-mail e um token", body, StringComparison.Ordinal);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(257, 32)]
    [InlineData(24, 4097)]
    public async Task PostConfirmEmail_WhenFieldExceedsLimit_ReturnsValidationProblem(
        int emailLength,
        int tokenLength)
    {
        var service = new FakeEmailConfirmationService(EmailConfirmationResult.Confirmed());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetRequestToken(client);

        var email = emailLength == 257
            ? $"{new string('a', emailLength - "@example.invalid".Length)}@example.invalid"
            : TestEmail;
        var token = new string('t', tokenLength);
        using var request = CreateRequest(email, token, antiforgeryToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task PostConfirmEmail_WithoutAntiforgery_IsRejectedBeforeCallingService()
    {
        var service = new FakeEmailConfirmationService(EmailConfirmationResult.Confirmed());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            AuthEndpoints.ConfirmEmailEndpoint,
            new ConfirmClientEmailRequest { Email = TestEmail, Token = TestToken });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task PostConfirmEmail_WhenServiceThrows_ReturnsGenericServerErrorWithoutSensitiveValues()
    {
        const string privateFailure = "private token and email details";
        var service = new FakeEmailConfirmationService(
            new InvalidOperationException(privateFailure));
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetRequestToken(client);

        using var request = CreateRequest(TestEmail, TestToken, antiforgeryToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("Não foi possível confirmar o e-mail neste momento.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(privateFailure, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestEmail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestToken, body, StringComparison.Ordinal);
        Assert.Equal(1, service.Calls);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeEmailConfirmationService service) =>
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
                services.RemoveAll<IEmailConfirmationService>();
                services.AddSingleton<IEmailConfirmationService>(service);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static HttpRequestMessage CreateRequest(string? email, string? token, string? requestToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.ConfirmEmailEndpoint)
        {
            Content = JsonContent.Create(new ConfirmClientEmailRequest { Email = email, Token = token })
        };
        if (requestToken is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);
        }

        return request;
    }

    private static async Task<string> GetRequestToken(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        return Assert.IsType<string>(body?.RootElement.GetProperty("requestToken").GetString());
    }

    private static EmailConfirmationResult ToResult(EmailConfirmationStatus status) =>
        status switch
        {
            EmailConfirmationStatus.UserNotFound => EmailConfirmationResult.UserNotFound(),
            EmailConfirmationStatus.InvalidOrExpiredToken => EmailConfirmationResult.InvalidOrExpiredToken(),
            EmailConfirmationStatus.AlreadyConfirmed => EmailConfirmationResult.AlreadyConfirmed(),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private sealed class FakeEmailConfirmationService : IEmailConfirmationService
    {
        private readonly EmailConfirmationResult? _result;
        private readonly Exception? _exception;

        public FakeEmailConfirmationService(EmailConfirmationResult result)
        {
            _result = result;
        }

        public FakeEmailConfirmationService(Exception exception)
        {
            _exception = exception;
        }

        public int Calls { get; private set; }
        public string? LastEmail { get; private set; }
        public string? LastToken { get; private set; }

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationResult> ConfirmEmailAsync(
            string email,
            string token,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastEmail = email;
            LastToken = token;
            return _exception is null
                ? Task.FromResult(_result ?? throw new InvalidOperationException("Fake confirmation result missing"))
                : Task.FromException<EmailConfirmationResult>(_exception);
        }
    }
}
