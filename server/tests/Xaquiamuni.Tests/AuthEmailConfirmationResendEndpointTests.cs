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
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Api.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class AuthEmailConfirmationResendEndpointTests
{
    private const string Endpoint = AuthEndpoints.ResendConfirmationEmailEndpoint;
    private const string TestEmail = "client@example.invalid";
    private const string GenericResponse =
        "Se houver uma conta elegível, enviaremos um e-mail de confirmação.";

    [Fact]
    public async Task PostResendConfirmationEmail_AnonymousWithAntiforgery_ReturnsGenericAccepted()
    {
        var useCase = new FakeResendConfirmationUseCase(
            SolicitarReenvioConfirmacaoEmailResult.RequestProcessed());
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
        Assert.Equal(1, useCase.Calls);
        Assert.Equal(TestEmail, useCase.LastEmail);
        Assert.True(useCase.LastCancellationToken.CanBeCanceled);
        Assert.Equal(["message"], properties.Select(property => property.Name).ToArray());
        Assert.Equal(GenericResponse, properties[0].Value.GetString());
        Assert.DoesNotContain(TestEmail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("UserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Outbox", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cooldown", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("limit", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-email")]
    [InlineData("client@example")]
    public async Task PostResendConfirmationEmail_InvalidEmail_ReturnsValidationProblem(
        string? email)
    {
        var useCase = new FakeResendConfirmationUseCase(
            SolicitarReenvioConfirmacaoEmailResult.InvalidEmail());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);

        using var request = CreateRequest(email, requestToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("\"Email\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestEmail, body, StringComparison.Ordinal);
        Assert.Equal(1, useCase.Calls);
        Assert.Equal(email, useCase.LastEmail);
    }

    [Fact]
    public async Task PostResendConfirmationEmail_EmailAboveLimit_ReturnsValidationProblem()
    {
        var email = $"{new string('a', 245)}@example.com";
        var useCase = new FakeResendConfirmationUseCase(
            SolicitarReenvioConfirmacaoEmailResult.InvalidEmail());
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
    public async Task PostResendConfirmationEmail_WithoutEmailField_ReturnsValidationProblem()
    {
        var useCase = new FakeResendConfirmationUseCase(
            SolicitarReenvioConfirmacaoEmailResult.InvalidEmail());
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

    [Fact]
    public async Task PostResendConfirmationEmail_MalformedJson_ReturnsBadRequestWithoutCallingUseCase()
    {
        var useCase = new FakeResendConfirmationUseCase(
            SolicitarReenvioConfirmacaoEmailResult.RequestProcessed());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        using var request = CreateRawRequest("{", requestToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, useCase.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostResendConfirmationEmail_WithoutValidAntiforgery_ReturnsBadRequestBeforeUseCase(
        bool useInvalidToken)
    {
        var useCase = new FakeResendConfirmationUseCase(
            SolicitarReenvioConfirmacaoEmailResult.RequestProcessed());
        using var factory = CreateFactory(useCase);
        using var client = CreateClient(factory);
        var requestToken = await GetRequestToken(client);
        using var request = CreateRequest(
            TestEmail,
            useInvalidToken ? "invalid-token" : null);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, useCase.Calls);
    }

    [Fact]
    public async Task PostResendConfirmationEmail_UnexpectedUseCaseFailureIsNotAccepted()
    {
        var expectedException = new InvalidOperationException("Private infrastructure details.");
        var useCase = new FakeResendConfirmationUseCase(expectedException);
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
            Content = JsonContent.Create(new ResendConfirmationEmailRequest { Email = email })
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

    private sealed class FakeResendConfirmationUseCase(
        SolicitarReenvioConfirmacaoEmailResult? result = null,
        Exception? exception = null) : ISolicitarReenvioConfirmacaoEmail
    {
        public FakeResendConfirmationUseCase(Exception exception)
            : this(result: null, exception: exception)
        {
        }

        public int Calls { get; private set; }
        public string? LastEmail { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<SolicitarReenvioConfirmacaoEmailResult> ExecuteAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastEmail = email;
            LastCancellationToken = cancellationToken;
            return exception is null
                ? Task.FromResult(result
                    ?? throw new InvalidOperationException("Fake use case result missing."))
                : Task.FromException<SolicitarReenvioConfirmacaoEmailResult>(exception);
        }
    }
}
