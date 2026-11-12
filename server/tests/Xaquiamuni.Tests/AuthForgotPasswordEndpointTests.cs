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

public sealed class AuthForgotPasswordEndpointTests
{
    private const string Endpoint = AuthEndpoints.ForgotPasswordEndpoint;
    private const string TestEmail = "cliente@example.com";
    private const string GenericResponse =
        "Se existir uma conta elegível para recuperação, você receberá instruções para redefinir sua senha.";

    [Fact]
    public async Task PostForgotPassword_ValidEmailReturnsGenericAcceptedWithoutAuthenticationCookie()
    {
        var useCase = new FakePasswordRecoveryUseCase(
            SolicitarRecuperacaoSenhaResult.RequestProcessed());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(TestEmail, requestToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        var properties = document.RootElement.EnumerateObject().ToArray();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["message"], properties.Select(property => property.Name).ToArray());
        Assert.Equal(GenericResponse, properties[0].Value.GetString());
        Assert.DoesNotContain(TestEmail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("UserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EmailConfirmed", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Outbox", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cooldown", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("limit", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, useCase.Calls);
        Assert.Equal(TestEmail, useCase.LastEmail);
        Assert.True(useCase.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task PostForgotPassword_AnonymousRequestDoesNotCreateAuthenticatedSession()
    {
        var useCase = new FakePasswordRecoveryUseCase(
            SolicitarRecuperacaoSenhaResult.RequestProcessed());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(TestEmail, requestToken);
        using var response = await client.SendAsync(request);
        using var currentClientResponse = await client.GetAsync(AuthEndpoints.MeEndpoint);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, currentClientResponse.StatusCode);
        Assert.Null(currentClientResponse.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, useCase.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-email")]
    [InlineData("cliente@example")]
    public async Task PostForgotPassword_InvalidEmailReturns400WithoutRevealingInput(string? email)
    {
        var useCase = new FakePasswordRecoveryUseCase(
            SolicitarRecuperacaoSenhaResult.InvalidEmail());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(email, requestToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("\"Email\"", body, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(email))
        {
            Assert.DoesNotContain(email, body, StringComparison.Ordinal);
        }

        Assert.Equal(1, useCase.Calls);
        Assert.Equal(email, useCase.LastEmail);
    }

    [Fact]
    public async Task PostForgotPassword_EmailAboveLimitReturns400WithoutRevealingInput()
    {
        var email = $"{new string('a', 245)}@example.com";
        var useCase = new FakePasswordRecoveryUseCase(
            SolicitarRecuperacaoSenhaResult.InvalidEmail());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(email, requestToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.DoesNotContain(email, body, StringComparison.Ordinal);
        Assert.Equal(1, useCase.Calls);
        Assert.Equal(email, useCase.LastEmail);
    }

    [Fact]
    public async Task PostForgotPassword_MissingEmailFieldReturns400()
    {
        var useCase = new FakePasswordRecoveryUseCase(
            SolicitarRecuperacaoSenhaResult.InvalidEmail());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRawRequest("{}", requestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(1, useCase.Calls);
        Assert.Null(useCase.LastEmail);
    }

    [Theory]
    [InlineData("{")]
    [InlineData(null)]
    public async Task PostForgotPassword_MalformedOrMissingBodyReturns400WithoutCallingUseCase(
        string? body)
    {
        var useCase = new FakePasswordRecoveryUseCase(
            SolicitarRecuperacaoSenhaResult.RequestProcessed());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        using var request = body is null
            ? new HttpRequestMessage(HttpMethod.Post, Endpoint)
            : CreateRawRequest(body, requestToken);
        AddRequestToken(request, body is null ? requestToken : null);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, useCase.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostForgotPassword_WithoutValidAntiforgeryReturns400BeforeUseCase(
        bool useInvalidToken)
    {
        var useCase = new FakePasswordRecoveryUseCase(
            SolicitarRecuperacaoSenhaResult.RequestProcessed());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        _ = await GetRequestToken(client);

        using var request = CreateRequest(TestEmail, null);
        if (useInvalidToken)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, "invalid-token");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, useCase.Calls);
    }

    [Fact]
    public async Task PostForgotPassword_UnexpectedUseCaseFailureIsNotAccepted()
    {
        var expectedException = new InvalidOperationException("Private infrastructure details.");
        var useCase = new FakePasswordRecoveryUseCase(expectedException);
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(TestEmail, requestToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request));

        Assert.Same(expectedException, exception);
        Assert.Equal(1, useCase.Calls);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakePasswordRecoveryUseCase useCase) =>
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
                services.AddSingleton<ISolicitarRecuperacaoSenha>(useCase);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static HttpRequestMessage CreateRequest(string? email, string? requestToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new ForgotPasswordRequest { Email = email })
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

    private sealed class FakePasswordRecoveryUseCase : ISolicitarRecuperacaoSenha
    {
        private readonly SolicitarRecuperacaoSenhaResult? _result;
        private readonly Exception? _exception;

        public FakePasswordRecoveryUseCase(SolicitarRecuperacaoSenhaResult result)
        {
            _result = result;
        }

        public FakePasswordRecoveryUseCase(Exception exception)
        {
            _exception = exception;
        }

        public int Calls { get; private set; }
        public string? LastEmail { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<SolicitarRecuperacaoSenhaResult> ExecuteAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastEmail = email;
            LastCancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_result
                    ?? throw new InvalidOperationException("Fake use case result missing."))
                : Task.FromException<SolicitarRecuperacaoSenhaResult>(_exception);
        }
    }
}
