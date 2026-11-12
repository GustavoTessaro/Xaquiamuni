using System.Net;
using System.Net.Http.Json;
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

public sealed class AuthResetPasswordEndpointTests
{
    private const string Endpoint = AuthEndpoints.ResetPasswordEndpoint;
    private const string TestEmail = "cliente@example.com";
    private const string TestToken = "CfDJ8+token/%2B=._~-encoded";
    private const string TestPassword = "Nova Senha Segura 987!";
    private const string GenericFailure =
        "Não foi possível redefinir a senha. O link de recuperação pode ser inválido ou ter expirado. Solicite uma nova recuperação.";

    [Fact]
    public async Task PostResetPassword_SuccessReturnsNoContentAndForwardsValuesAndCancellation()
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        var requestBody = new ResetPasswordRequest
        {
            Email = TestEmail,
            Token = TestToken,
            NewPassword = TestPassword
        };

        using var request = CreateRequest(requestBody, requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStore(response);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, service.ResetCalls);
        Assert.Equal(TestEmail, service.LastEmail);
        Assert.Equal(TestToken, service.LastToken);
        Assert.Equal(TestPassword, service.LastNewPassword);
        Assert.True(service.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task PostResetPassword_AnonymousSuccessDoesNotCreateAuthenticatedSession()
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), requestToken);
        using var response = await client.SendAsync(request);
        using var currentClientResponse = await client.GetAsync(AuthEndpoints.MeEndpoint);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, currentClientResponse.StatusCode);
        Assert.Null(currentClientResponse.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_InvalidRequestReturnsGenericResponse()
    {
        var service = new FakePasswordResetService(PasswordResetResult.InvalidRequest());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var firstRequest = CreateRequest(
            ValidRequest() with { Token = "invalid-token" },
            requestToken);
        using var firstResponse = await client.SendAsync(firstRequest);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();

        using var secondRequest = CreateRequest(
            ValidRequest() with { Token = "different-invalid-token" },
            requestToken);
        using var secondResponse = await client.SendAsync(secondRequest);
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        AssertNoStore(firstResponse);
        AssertNoStore(secondResponse);
        Assert.Equal(firstBody, secondBody);
        Assert.Contains(GenericFailure, firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain(TestEmail, firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid-token", firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPassword, firstBody, StringComparison.Ordinal);
        Assert.Equal(2, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_InvalidPasswordReturnsSafeValidationProblem()
    {
        const string invalidPassword = "weak password";
        var service = new FakePasswordResetService(PasswordResetResult.InvalidPassword());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(
            ValidRequest() with { NewPassword = invalidPassword },
            requestToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("NewPassword", body, StringComparison.Ordinal);
        Assert.Contains("A senha não atende aos requisitos de segurança.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestEmail, body, StringComparison.Ordinal);
        Assert.Equal(1, service.ResetCalls);
        Assert.Equal(invalidPassword, service.LastNewPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-email")]
    [InlineData("cliente@example")]
    public async Task PostResetPassword_InvalidEmailReturns400WithoutCallingService(string? email)
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest() with { Email = email }, requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_EmailAboveLimitReturns400WithoutCallingService()
    {
        var email = $"{new string('a', 245)}@example.com";
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest() with { Email = email }, requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_MissingEmailFieldReturns400WithoutCallingService()
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        var json = JsonSerializer.Serialize(new
        {
            token = TestToken,
            newPassword = TestPassword
        });

        using var request = CreateRawRequest(json, requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostResetPassword_MissingOrBlankTokenReturns400WithoutCallingService(
        string? token)
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest() with { Token = token }, requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_MissingTokenFieldReturns400WithoutCallingService()
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        var json = JsonSerializer.Serialize(new
        {
            email = TestEmail,
            newPassword = TestPassword
        });

        using var request = CreateRawRequest(json, requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_TokenAboveLimitReturns400WithoutCallingService()
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(
            ValidRequest() with { Token = new string('t', ResetPasswordRequest.TokenMaxLength + 1) },
            requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task PostResetPassword_MissingOrEmptyPasswordReturns400WithoutCallingService(
        string? newPassword)
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(
            ValidRequest() with { NewPassword = newPassword },
            requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_MissingPasswordFieldReturns400WithoutCallingService()
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        var json = JsonSerializer.Serialize(new
        {
            email = TestEmail,
            token = TestToken
        });

        using var request = CreateRawRequest(json, requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_PasswordAboveLimitReturns400WithoutCallingService()
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(
            ValidRequest() with { NewPassword = new string('A', ResetPasswordRequest.NewPasswordMaxLength + 1) },
            requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Theory]
    [InlineData("{")]
    [InlineData(null)]
    public async Task PostResetPassword_MalformedOrMissingBodyReturns400WithoutCallingService(
        string? body)
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        using var request = body is null
            ? new HttpRequestMessage(HttpMethod.Post, Endpoint)
            : CreateRawRequest(body, requestToken);
        AddRequestToken(request, body is null ? requestToken : null);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostResetPassword_WithoutValidAntiforgeryReturns400BeforeService(
        bool useInvalidToken)
    {
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        _ = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), null);
        if (useInvalidToken)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, "invalid-token");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_UnexpectedServiceFailureIsNotAccepted()
    {
        var expectedException = new InvalidOperationException("Private token and password details.");
        var service = new FakePasswordResetService(expectedException);
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), requestToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request));

        Assert.Same(expectedException, exception);
        Assert.Equal(1, service.ResetCalls);
    }

    [Fact]
    public async Task PostResetPassword_PreservesTokenAndPasswordContentsWithoutDoubleDecoding()
    {
        const string token = "CfDJ8+/?%2B%2F=._~-";
        const string password = "  Nova Senha 987!  ";
        const string email = "  cliente@example.com  ";
        var service = new FakePasswordResetService(PasswordResetResult.Success());
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(
            new ResetPasswordRequest
            {
                Email = email,
                Token = token,
                NewPassword = password
            },
            requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(email, service.LastEmail);
        Assert.Equal(token, service.LastToken);
        Assert.Equal(password, service.LastNewPassword);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakePasswordResetService service) =>
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
                services.RemoveAll<IPasswordResetService>();
                services.AddSingleton<IPasswordResetService>(service);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static ResetPasswordRequest ValidRequest() =>
        new()
        {
            Email = TestEmail,
            Token = TestToken,
            NewPassword = TestPassword
        };

    private static HttpRequestMessage CreateRequest(
        ResetPasswordRequest resetRequest,
        string? requestToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(resetRequest)
        };
        AddRequestToken(request, requestToken);
        return request;
    }

    private static HttpRequestMessage CreateRawRequest(string json, string? requestToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        AddRequestToken(request, requestToken);
        return request;
    }

    private static void AddRequestToken(HttpRequestMessage request, string? requestToken)
    {
        if (requestToken is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);
        }
    }

    private static async Task<string> GetRequestToken(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return Assert.IsType<string>(body?.RootElement.GetProperty("requestToken").GetString());
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private sealed class FakePasswordResetService : IPasswordResetService
    {
        private readonly PasswordResetResult? _result;
        private readonly Exception? _exception;

        public FakePasswordResetService(PasswordResetResult result)
        {
            _result = result;
        }

        public FakePasswordResetService(Exception exception)
        {
            _exception = exception;
        }

        public int ResetCalls { get; private set; }
        public string? LastEmail { get; private set; }
        public string? LastToken { get; private set; }
        public string? LastNewPassword { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

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
            ResetCalls++;
            LastEmail = email;
            LastToken = token;
            LastNewPassword = newPassword;
            LastCancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_result
                    ?? throw new InvalidOperationException("Fake password reset result missing."))
                : Task.FromException<PasswordResetResult>(_exception);
        }
    }
}
