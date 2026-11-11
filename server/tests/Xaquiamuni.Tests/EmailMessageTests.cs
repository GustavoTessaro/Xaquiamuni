using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class EmailMessageTests
{
    [Fact]
    public void IEmailSender_ExposesAsyncSendWithCancellation()
    {
        var method = typeof(IEmailSender).GetMethod(nameof(IEmailSender.SendAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Collection(
            method.GetParameters(),
            parameter => Assert.Equal(typeof(EmailMessage), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public void Constructor_TrimsAndPreservesStructuredMessageFields()
    {
        var message = new EmailMessage(
            " recipient@example.invalid ",
            "Confirme seu e-mail",
            "<a href=\"https://example.invalid/confirm#sensitive-token\">Confirmar</a>");

        Assert.Equal("recipient@example.invalid", message.Recipient);
        Assert.Equal("Confirme seu e-mail", message.Subject);
        Assert.Contains("sensitive-token", message.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@domain")]
    public void Constructor_RejectsInvalidRecipient(string recipient)
    {
        Assert.Throws<ArgumentException>(
            () => new EmailMessage(recipient, "Subject", "Content"));
    }

    [Fact]
    public void Constructor_RejectsRecipientAboveLimit()
    {
        var recipient = $"{new string('a', 244)}@example.invalid";

        Assert.True(recipient.Length > EmailMessage.RecipientMaxLength);
        Assert.Throws<ArgumentException>(
            () => new EmailMessage(recipient, "Subject", "Content"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Constructor_RejectsEmptySubject(string subject)
    {
        Assert.Throws<ArgumentException>(
            () => new EmailMessage("recipient@example.invalid", subject, "Content"));
    }

    [Fact]
    public void Constructor_RejectsSubjectAboveLimitOrWithHeaderLineBreaks()
    {
        Assert.Throws<ArgumentException>(
            () => new EmailMessage(
                "recipient@example.invalid",
                new string('s', EmailMessage.SubjectMaxLength + 1),
                "Content"));
        Assert.Throws<ArgumentException>(
            () => new EmailMessage("recipient@example.invalid", "Subject\r\nBcc: other@example.invalid", "Content"));
    }

    [Fact]
    public void Constructor_RejectsEmptyContent()
    {
        Assert.Throws<ArgumentException>(
            () => new EmailMessage("recipient@example.invalid", "Subject", " "));
    }

    [Fact]
    public void ToString_DoesNotExposeRecipientSubjectOrContent()
    {
        const string recipient = "private-recipient@example.invalid";
        const string subject = "private subject";
        const string content = "https://example.invalid/confirm#private-token";
        var message = new EmailMessage(recipient, subject, content);

        var text = message.ToString();

        Assert.DoesNotContain(recipient, text, StringComparison.Ordinal);
        Assert.DoesNotContain(subject, text, StringComparison.Ordinal);
        Assert.DoesNotContain(content, text, StringComparison.Ordinal);
    }
}
