using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class EmailConfirmationMessageComposerTests
{
    [Theory]
    [InlineData("Development", "http://localhost:4200", "http://localhost:4200/confirm-email")]
    [InlineData("Production", "https://frontend.example.invalid/app/", "https://frontend.example.invalid/app/confirm-email")]
    public void Constructor_AcceptsAllowedBaseUrl(
        string environmentName,
        string baseUrl,
        string expectedPageUrl)
    {
        var composer = CreateComposer(baseUrl, environmentName);
        var message = composer.Compose(
            EmailConfirmationTokenResult.GeneratedForUser("client@example.invalid", "test-token"));

        Assert.Contains(expectedPageUrl, WebUtility.HtmlDecode(message.Content), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", "http://frontend.example.invalid")]
    [InlineData("Production", "ftp://frontend.example.invalid")]
    [InlineData("Production", "/confirm-email")]
    [InlineData("Production", "https://user:password@frontend.example.invalid")]
    [InlineData("Production", "https://frontend.example.invalid?mode=confirm")]
    [InlineData("Production", "https://frontend.example.invalid?")]
    [InlineData("Production", "https://frontend.example.invalid#existing-fragment")]
    [InlineData("Production", "https://frontend.example.invalid#")]
    [InlineData("Development", "ftp://frontend.example.invalid")]
    [InlineData("Development", "https://frontend.example.invalid/path with spaces")]
    [InlineData("Production", "")]
    public void Constructor_RejectsInvalidOrDisallowedBaseUrl(
        string environmentName,
        string baseUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateComposer(baseUrl, environmentName));

        if (!string.IsNullOrEmpty(baseUrl))
        {
            Assert.DoesNotContain(baseUrl, exception.Message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_EncodesEmailAndTokenInFragmentAndHtmlEncodesLink()
    {
        const string email = "client+tag@example.invalid";
        const string token = "token+with/= symbols&?#";
        var composer = CreateComposer("https://frontend.example.invalid", "Production");

        var message = composer.Compose(EmailConfirmationTokenResult.GeneratedForUser(email, token));
        var html = WebUtility.HtmlDecode(message.Content);
        var hrefStart = html.IndexOf("href=\"", StringComparison.Ordinal) + "href=\"".Length;
        var hrefEnd = html.IndexOf('"', hrefStart);
        var link = html[hrefStart..hrefEnd];
        var uri = new Uri(link);

        Assert.Equal(email, message.Recipient);
        Assert.Equal("Confirme seu e-mail", message.Subject);
        Assert.StartsWith("https://frontend.example.invalid/confirm-email#", link, StringComparison.Ordinal);
        Assert.Equal(
            $"#email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}",
            uri.Fragment);
        Assert.DoesNotContain(token, message.Content, StringComparison.Ordinal);
        Assert.Contains("&amp;", message.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(email, message.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, message.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_RejectsResultWithoutUsableEmailAndTokenWithoutLeakingValues()
    {
        const string email = "private email value";
        const string token = "private-token-value";
        var composer = CreateComposer("https://frontend.example.invalid", "Production");

        var exception = Assert.Throws<ArgumentException>(
            () => composer.Compose(EmailConfirmationTokenResult.GeneratedForUser(email, token)));

        Assert.DoesNotContain(email, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("frontend.example.invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_RejectsResultsWithoutEmailWithoutLeakingTokenOrLink()
    {
        const string token = "private-token-value";
        var composer = CreateComposer("https://frontend.example.invalid", "Production");

        var exception = Assert.Throws<InvalidOperationException>(
            () => composer.Compose(EmailConfirmationTokenResult.Generated(token)));

        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("frontend.example.invalid", exception.Message, StringComparison.Ordinal);
    }

    private static EmailConfirmationMessageComposer CreateComposer(string baseUrl, string environmentName) =>
        new(
            Options.Create(new EmailConfirmationEmailOptions { PublicFrontendBaseUrl = baseUrl }),
            new TestHostEnvironment(environmentName));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = nameof(EmailConfirmationMessageComposerTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
