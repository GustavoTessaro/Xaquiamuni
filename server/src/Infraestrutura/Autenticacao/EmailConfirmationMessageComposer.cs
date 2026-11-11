using System.Text.Encodings.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Autenticacao;

public sealed class EmailConfirmationMessageComposer : IEmailConfirmationMessageComposer
{
    private const string ConfirmationPagePath = "confirm-email";
    private const string Subject = "Confirme seu e-mail";
    private readonly Uri _confirmationPageUri;

    public EmailConfirmationMessageComposer(
        IOptions<EmailConfirmationEmailOptions> options,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        _confirmationPageUri = GetConfirmationPageUri(
            options.Value.PublicFrontendBaseUrl,
            environment.IsDevelopment());
    }

    public EmailMessage Compose(EmailConfirmationTokenResult tokenResult)
    {
        ArgumentNullException.ThrowIfNull(tokenResult);

        if (tokenResult.Status != EmailConfirmationTokenStatus.Generated
            || string.IsNullOrWhiteSpace(tokenResult.Email)
            || string.IsNullOrWhiteSpace(tokenResult.Token))
        {
            throw new InvalidOperationException(
                "An email confirmation message cannot be composed from the supplied result.");
        }

        var fragment = $"email={Uri.EscapeDataString(tokenResult.Email)}"
            + $"&token={Uri.EscapeDataString(tokenResult.Token)}";
        var link = $"{_confirmationPageUri.GetLeftPart(UriPartial.Path)}#{fragment}";
        var encodedLink = HtmlEncoder.Default.Encode(link);
        var content = $"<p>Para confirmar seu e-mail, acesse o link abaixo:</p>"
            + $"<p><a href=\"{encodedLink}\">Confirmar e-mail</a></p>";

        return new EmailMessage(tokenResult.Email, Subject, content);
    }

    private static Uri GetConfirmationPageUri(string? configuredBaseUrl, bool isDevelopment)
    {
        var baseUrl = configuredBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl)
            || baseUrl.Contains('?')
            || baseUrl.Contains('#')
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(isDevelopment && uri.Scheme == Uri.UriSchemeHttp))
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                isDevelopment
                    ? "The public frontend URL for email confirmation is invalid."
                    : "The public frontend URL for email confirmation must be a valid HTTPS URL.");
        }

        var normalizedBaseUrl = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBaseUrl, ConfirmationPagePath);
    }
}
