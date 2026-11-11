using System.Net.Mail;

namespace Xaquiamuni.Infraestrutura.Autenticacao;

public sealed class ResendEmailOptions
{
    public const string SectionName = "ResendEmail";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string? FromEmail { get; set; }
}

internal static class ResendEmailOptionsValidation
{
    public static bool IsApiKeyValid(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        var paddingStarted = false;
        var hasTokenCharacter = false;
        foreach (var character in apiKey)
        {
            if (character == '=')
            {
                paddingStarted = true;
                continue;
            }

            hasTokenCharacter = true;
            if (paddingStarted
                || !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_' or '.' or '~' or '+' or '/'))
            {
                return false;
            }
        }

        return hasTokenCharacter;
    }

    public static bool IsFromEmailValid(string? fromEmail) =>
        !string.IsNullOrWhiteSpace(fromEmail)
        && string.Equals(fromEmail, fromEmail.Trim(), StringComparison.Ordinal)
        && MailAddress.TryCreate(fromEmail, out var address)
        && string.Equals(address.Address, fromEmail, StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.', StringComparison.Ordinal)
        && !address.Host.StartsWith(".", StringComparison.Ordinal)
        && !address.Host.EndsWith(".", StringComparison.Ordinal)
        && !address.Host.Contains("..", StringComparison.Ordinal);

    public static string[] GetFailures(ResendEmailOptions options)
    {
        var failures = new List<string>();
        if (!IsApiKeyValid(options.ApiKey))
        {
            failures.Add("ApiKey is required and must be a single token.");
        }

        if (!IsFromEmailValid(options.FromEmail))
        {
            failures.Add("FromEmail must be a valid email address.");
        }

        return failures.ToArray();
    }
}
