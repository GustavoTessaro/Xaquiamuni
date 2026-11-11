using System.Net.Mail;

namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class EmailMessage
{
    public const int RecipientMaxLength = 256;
    public const int SubjectMaxLength = 256;

    public EmailMessage(string recipient, string subject, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var normalizedRecipient = recipient.Trim();
        if (normalizedRecipient.Length > RecipientMaxLength
            || !MailAddress.TryCreate(normalizedRecipient, out var address)
            || !string.Equals(address.Address, normalizedRecipient, StringComparison.OrdinalIgnoreCase)
            || !HasDottedDomain(normalizedRecipient))
        {
            throw new ArgumentException("Recipient must be a valid email address.", nameof(recipient));
        }

        if (subject.Length > SubjectMaxLength
            || subject.Contains('\r')
            || subject.Contains('\n'))
        {
            throw new ArgumentException("Subject is invalid.", nameof(subject));
        }

        Recipient = normalizedRecipient;
        Subject = subject;
        Content = content;
    }

    public string Recipient { get; }
    public string Subject { get; }
    public string Content { get; }

    public override string ToString() => "[EmailMessage]";

    private static bool HasDottedDomain(string email)
    {
        var separator = email.LastIndexOf('@');
        if (separator <= 0 || separator == email.Length - 1)
        {
            return false;
        }

        var domain = email.AsSpan(separator + 1);
        return domain.Contains('.')
            && domain[0] != '.'
            && domain[^1] != '.'
            && !domain.Contains("..", StringComparison.Ordinal);
    }
}
