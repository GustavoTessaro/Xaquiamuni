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
        var content = "<div style=\"margin:0 auto;max-width:600px;font-family:Arial,sans-serif;color:#24312b;line-height:1.6\">"
            + "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;margin:0 auto;border-collapse:collapse;background-color:#ffffff\">"
            + "<tr><td style=\"padding:32px 24px\">"
            + "<h1 style=\"margin:0 0 16px;color:#28734b;font-size:24px\">Xaquiamuni Produtos Naturais</h1>"
            + "<h2 style=\"margin:0 0 16px;color:#24312b;font-size:20px\">Bem-vindo! Confirme seu e-mail</h2>"
            + "<p style=\"margin:0 0 20px\">A confirmação permite validar o endereço de e-mail cadastrado na sua conta.</p>"
            + "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-collapse:collapse\">"
            + $"<tr><td align=\"center\" bgcolor=\"#28734b\" style=\"border-radius:4px\"><a href=\"{encodedLink}\" style=\"display:inline-block;background:#28734b;color:#fff;padding:12px 20px;text-decoration:none;border-radius:4px;font-weight:bold\">Confirmar meu e-mail</a></td></tr>"
            + "</table>"
            + "<p style=\"margin:20px 0 12px\">Este link é válido por 24 horas.</p>"
            + "<p style=\"margin:0\">Se você não realizou este cadastro, ignore este e-mail.</p>"
            + "</td></tr></table></div>";

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
