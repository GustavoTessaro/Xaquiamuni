using System.Text.Encodings.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Autenticacao;

public sealed class PasswordResetMessageComposer : IPasswordResetMessageComposer
{
    private const string ResetPagePath = "reset-password";
    private const string Subject = "Redefinição de senha — Xaquiamuni";
    private readonly Uri _resetPageUri;

    public PasswordResetMessageComposer(
        IOptions<EmailConfirmationEmailOptions> options,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        _resetPageUri = GetResetPageUri(
            options.Value.PublicFrontendBaseUrl,
            environment.IsDevelopment());
    }

    public EmailMessage Compose(PasswordResetTokenResult tokenResult)
    {
        ArgumentNullException.ThrowIfNull(tokenResult);

        if (tokenResult.Status != PasswordResetTokenStatus.Generated
            || string.IsNullOrWhiteSpace(tokenResult.Email)
            || string.IsNullOrWhiteSpace(tokenResult.Token))
        {
            throw new InvalidOperationException(
                "A password reset message cannot be composed from the supplied result.");
        }

        var recipient = tokenResult.Email.Trim();
        var fragment = $"email={Uri.EscapeDataString(recipient)}"
            + $"&token={Uri.EscapeDataString(tokenResult.Token)}";
        var link = $"{_resetPageUri.GetLeftPart(UriPartial.Path)}#{fragment}";
        var encodedLink = HtmlEncoder.Default.Encode(link);
        var content = "<div style=\"font-family:Arial,sans-serif;color:#24312b;line-height:1.6\">"
            + "<h1 style=\"color:#28734b\">Xaquiamuni Produtos Naturais</h1>"
            + "<p>Foi solicitada uma redefinição de senha para sua conta.</p>"
            + $"<p><a href=\"{encodedLink}\" style=\"display:inline-block;background:#28734b;color:#fff;padding:12px 20px;text-decoration:none;border-radius:4px\">Redefinir minha senha</a></p>"
            + "<p>Este link é válido por 1 hora.</p>"
            + "<p>Se você não solicitou esta redefinição, ignore este e-mail. Sua senha permanecerá inalterada.</p>"
            + "</div>";

        return new EmailMessage(recipient, Subject, content);
    }

    private static Uri GetResetPageUri(string? configuredBaseUrl, bool isDevelopment)
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
                    ? "The public frontend URL for password reset is invalid."
                    : "The public frontend URL for password reset must be a valid HTTPS URL.");
        }

        var normalizedBaseUrl = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBaseUrl, ResetPagePath);
    }
}
