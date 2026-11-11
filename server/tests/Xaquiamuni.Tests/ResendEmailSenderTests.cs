using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class ResendEmailSenderTests
{
    private const string ApiKey = "re_test_only_key";
    private static readonly ResendEmailOptions ValidOptions = new()
    {
        ApiKey = ApiKey,
        FromEmail = "no-reply@example.test"
    };

    [Fact]
    public async Task SendAsync_PostsEmailPayloadWithBearerAuthentication()
    {
        HttpMethod? method = null;
        Uri? requestUri = null;
        string? authorizationScheme = null;
        string? authorizationParameter = null;
        string? requestBody = null;
        using var client = CreateHttpClient(async (request, _) =>
        {
            method = request.Method;
            requestUri = request.RequestUri;
            authorizationScheme = request.Headers.Authorization?.Scheme;
            authorizationParameter = request.Headers.Authorization?.Parameter;
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var sender = new ResendEmailSender(client, Options.Create(ValidOptions));
        var message = CreateMessage();

        await sender.SendAsync(message);

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("https://api.resend.com/emails", requestUri?.AbsoluteUri);
        Assert.Equal("Bearer", authorizationScheme);
        Assert.Equal(ApiKey, authorizationParameter);
        using var payload = JsonDocument.Parse(requestBody!);
        var root = payload.RootElement;
        Assert.Equal("no-reply@example.test", root.GetProperty("from").GetString());
        Assert.Equal(
            "recipient@example.test",
            root.GetProperty("to")[0].GetString());
        Assert.Equal(message.Subject, root.GetProperty("subject").GetString());
        Assert.Equal(message.Content, root.GetProperty("html").GetString());
        Assert.DoesNotContain(ApiKey, requestBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task SendAsync_AcceptsAnySuccessStatusCode(HttpStatusCode statusCode)
    {
        using var client = CreateHttpClient(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        var sender = new ResendEmailSender(client, Options.Create(ValidOptions));

        await sender.SendAsync(CreateMessage());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData((HttpStatusCode)600, false)]
    public async Task SendAsync_ClassifiesHttpErrorsWithoutExposingResponseBody(
        HttpStatusCode statusCode,
        bool expectedTransient)
    {
        const string sensitiveResponse = "recipient@example.test private-token";
        using var client = CreateHttpClient(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(sensitiveResponse)
            }));
        var sender = new ResendEmailSender(client, Options.Create(ValidOptions));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(CreateMessage()));

        Assert.Equal(expectedTransient, exception.IsTransient);
        Assert.DoesNotContain(sensitiveResponse, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_MapsNetworkFailuresToTransientWithoutRetainingDetails()
    {
        const string sensitiveFailure = "recipient@example.test private-token";
        using var client = CreateHttpClient(
            (_, _) => Task.FromException<HttpResponseMessage>(
                new HttpRequestException(sensitiveFailure)));
        var sender = new ResendEmailSender(client, Options.Create(ValidOptions));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(CreateMessage()));

        Assert.True(exception.IsTransient);
        Assert.DoesNotContain(sensitiveFailure, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_MapsHttpClientTimeoutToTransientFailure()
    {
        using var client = CreateHttpClient(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(50));
        var sender = new ResendEmailSender(client, Options.Create(ValidOptions));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(CreateMessage()));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task SendAsync_PropagatesCallerCancellation()
    {
        var handlerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateHttpClient(
            async (_, cancellationToken) =>
            {
                handlerStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        var sender = new ResendEmailSender(client, Options.Create(ValidOptions));
        using var cancellation = new CancellationTokenSource();

        var sendTask = sender.SendAsync(CreateMessage(), cancellation.Token);
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
    }

    [Theory]
    [InlineData("", "no-reply@example.test")]
    [InlineData("=", "no-reply@example.test")]
    [InlineData("re_test_key", "invalid-address")]
    public void AddResendEmailSender_RejectsIncompleteConfiguration(
        string apiKey,
        string fromEmail)
    {
        var configuration = CreateConfiguration(
            ("ApiKey", apiKey),
            ("FromEmail", fromEmail));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResendEmailSender(configuration);

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IEmailSender>());

        if (!string.IsNullOrEmpty(apiKey))
        {
            Assert.DoesNotContain(apiKey, exception.Message, StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(fromEmail))
        {
            Assert.DoesNotContain(fromEmail, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AddInfrastructureServices_DoesNotRegisterResendSenderAutomatically()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IEmailSender));
    }

    private static EmailMessage CreateMessage() =>
        new(
            "recipient@example.test",
            "Confirm your address",
            "<p>Use this private-token to confirm.</p>");

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler))
        {
            Timeout = timeout ?? ResendEmailOptions.RequestTimeout
        };
        return httpClient;
    }

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(
                item => $"{ResendEmailOptions.SectionName}:{item.Key}",
                item => (string?)item.Value))
            .Build();

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
