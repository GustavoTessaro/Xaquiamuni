using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Autenticacao;

public sealed class ResendEmailSender : IEmailSender
{
    private static readonly Uri SendEmailEndpoint = new("https://api.resend.com/emails");
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _fromEmail;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendEmailOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);

        var configuredOptions = options.Value;
        var failures = ResendEmailOptionsValidation.GetFailures(configuredOptions);
        if (failures.Length > 0)
        {
            throw new OptionsValidationException(
                nameof(ResendEmailOptions),
                typeof(ResendEmailOptions),
                failures);
        }

        _apiKey = configuredOptions.ApiKey!;
        _fromEmail = configuredOptions.FromEmail!;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(HttpMethod.Post, SendEmailEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent.Create(new ResendEmailPayload(
            _fromEmail,
            [message.Recipient],
            message.Subject,
            message.Content));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new EmailDeliveryException(isTransient: true);
        }
        catch (HttpRequestException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (HttpRequestException)
        {
            throw new EmailDeliveryException(isTransient: true);
        }

        using (response)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
            {
                var isTransient = response.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode is >= 500 and <= 599;
                throw new EmailDeliveryException(isTransient);
            }
        }
    }

    private sealed record ResendEmailPayload(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html)
    {
        public override string ToString() => "[ResendEmailPayload]";
    }
}
