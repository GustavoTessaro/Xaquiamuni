using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Outbox;

namespace Xaquiamuni.Tests;

public sealed class PasswordResetMessageComposerTests
{
    [Theory]
    [InlineData("Development", "http://localhost:4200", "http://localhost:4200/reset-password")]
    [InlineData("Production", "https://frontend.example.invalid/app/", "https://frontend.example.invalid/app/reset-password")]
    [InlineData("Production", "https://frontend.example.invalid/app", "https://frontend.example.invalid/app/reset-password")]
    public void Compose_UsesConfiguredResetRouteAndPreservesBasePath(
        string environmentName,
        string baseUrl,
        string expectedPageUrl)
    {
        var message = CreateComposer(baseUrl, environmentName)
            .Compose(PasswordResetTokenResult.Generated("client@example.invalid", "synthetic-token"));

        Assert.Equal("client@example.invalid", message.Recipient);
        Assert.Equal("Redefinição de senha — Xaquiamuni", message.Subject);
        Assert.Contains(expectedPageUrl, WebUtility.HtmlDecode(message.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_IncludesButtonExpirationAndSecurityGuidance()
    {
        var message = CreateComposer("https://frontend.example.invalid", "Production")
            .Compose(PasswordResetTokenResult.Generated("client@example.invalid", "synthetic-token"));

        Assert.Contains(">Redefinir minha senha</a>", message.Content, StringComparison.Ordinal);
        Assert.Contains("Foi solicitada uma redefinição de senha", message.Content, StringComparison.Ordinal);
        Assert.Contains("válido por 1 hora", message.Content, StringComparison.Ordinal);
        Assert.Contains("ignore este e-mail", message.Content, StringComparison.Ordinal);
        Assert.Contains("Xaquiamuni Produtos Naturais", message.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurityStamp", message.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialGenerationFingerprint", message.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_EncodesEmailAndTokenOnlyInFragmentAndHtmlEncodesLink()
    {
        const string email = "client+tag@example.invalid";
        const string token = "token+with/= symbols&?#%<>\"'`";
        var message = CreateComposer("https://frontend.example.invalid", "Production")
            .Compose(PasswordResetTokenResult.Generated(email, token));

        var decodedHtml = WebUtility.HtmlDecode(message.Content);
        var link = ExtractLink(decodedHtml);
        var uri = new Uri(link);
        var fragmentValues = uri.Fragment[1..]
            .Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));

        Assert.Equal("https://frontend.example.invalid/reset-password", uri.GetLeftPart(UriPartial.Path));
        Assert.Empty(uri.Query);
        Assert.Equal(email, fragmentValues["email"]);
        Assert.Equal(token, fragmentValues["token"]);
        Assert.DoesNotContain(email, message.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(token, message.Content, StringComparison.Ordinal);
        Assert.Contains("&amp;", message.Content, StringComparison.Ordinal);
        Assert.Contains(
            $"#email={Uri.EscapeDataString(email)}&amp;token={Uri.EscapeDataString(token)}",
            message.Content,
            StringComparison.Ordinal);
        Assert.DoesNotContain(email, message.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, message.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Production", "not a url")]
    [InlineData("Production", "/reset-password")]
    [InlineData("Production", "http://frontend.example.invalid")]
    [InlineData("Production", "ftp://frontend.example.invalid")]
    [InlineData("Production", "https://user:password@frontend.example.invalid")]
    [InlineData("Production", "https://frontend.example.invalid?mode=reset")]
    [InlineData("Production", "https://frontend.example.invalid?")]
    [InlineData("Production", "https://frontend.example.invalid#existing")]
    [InlineData("Production", "https://frontend.example.invalid#")]
    [InlineData("Development", "ftp://frontend.example.invalid")]
    [InlineData("Development", "https://frontend.example.invalid/path with spaces")]
    public void Constructor_RejectsInvalidOrDisallowedUrlsWithoutLeakingConfiguration(
        string environmentName,
        string? baseUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateComposer(baseUrl, environmentName));

        if (!string.IsNullOrEmpty(baseUrl))
        {
            Assert.DoesNotContain(baseUrl, exception.Message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("frontend.example.invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_AllowsHttpOnlyInDevelopment()
    {
        var composer = CreateComposer("http://frontend.example.invalid/app", "Development");

        var message = composer.Compose(
            PasswordResetTokenResult.Generated("client@example.invalid", "synthetic-token"));

        Assert.Contains(
            "http://frontend.example.invalid/app/reset-password#",
            WebUtility.HtmlDecode(message.Content),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_RejectsInvalidInputWithoutLeakingEmailOrToken()
    {
        const string email = "private invalid email";
        const string token = "synthetic-private-token";
        var composer = CreateComposer("https://frontend.example.invalid", "Production");

        var invalidRecipient = Assert.Throws<ArgumentException>(
            () => composer.Compose(PasswordResetTokenResult.Generated(email, token)));
        var ineligibleResult = Assert.Throws<InvalidOperationException>(
            () => composer.Compose(PasswordResetTokenResult.NotEligible()));

        Assert.DoesNotContain(email, invalidRecipient.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, invalidRecipient.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("frontend.example.invalid", invalidRecipient.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, ineligibleResult.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("frontend.example.invalid", ineligibleResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_RegistersLazySingletonWithoutRequiringResetConfiguration()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Services.AddInfrastructureServices(new ConfigurationBuilder().Build());

        using var app = builder.Build();

        Assert.Null(
            app.Services.GetRequiredService<IOptions<EmailConfirmationEmailOptions>>()
                .Value.PublicFrontendBaseUrl);
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(builder.Services, descriptor =>
                descriptor.ServiceType == typeof(IPasswordResetMessageComposer)).Lifetime);
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(EmailOutboxDispatcher));
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(EmailConfirmationOutboxProcessor));
    }

    private static PasswordResetMessageComposer CreateComposer(
        string? baseUrl,
        string environmentName) =>
        new(
            Options.Create(new EmailConfirmationEmailOptions { PublicFrontendBaseUrl = baseUrl }),
            new TestHostEnvironment(environmentName));

    private static string ExtractLink(string html)
    {
        var hrefStart = html.IndexOf("href=\"", StringComparison.Ordinal) + "href=\"".Length;
        var hrefEnd = html.IndexOf('"', hrefStart);
        return html[hrefStart..hrefEnd];
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = nameof(PasswordResetMessageComposerTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
