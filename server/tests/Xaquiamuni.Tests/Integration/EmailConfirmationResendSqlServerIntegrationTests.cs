using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class EmailConfirmationResendSqlServerIntegrationTests
{
    private const string FrontendBaseUrl = "https://frontend.example.test";
    private const string SenderAddress = "no-reply@example.test";
    private const string SyntheticApiKey = "re_sql_integration_test_key";

    [SqlServerIntegrationFact]
    public Task SuccessfulDelivery_CompletesOutboxWithoutPersistingEmailPayload() =>
        RunPipelineAsync(
            HttpStatusCode.Accepted,
            EmailOutboxMessageStatus.Completed);

    [SqlServerIntegrationFact]
    public Task RateLimit_RetriesOutboxWithoutPersistingEmailPayload() =>
        RunPipelineAsync(
            HttpStatusCode.TooManyRequests,
            EmailOutboxMessageStatus.Pending);

    [SqlServerIntegrationFact]
    public Task PermanentRejection_DeadLettersOutboxWithoutPersistingEmailPayload() =>
        RunPipelineAsync(
            HttpStatusCode.BadRequest,
            EmailOutboxMessageStatus.DeadLetter);

    private static async Task RunPipelineAsync(
        HttpStatusCode resendStatusCode,
        EmailOutboxMessageStatus expectedOutboxStatus)
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var email = $"resend-integration-{Guid.NewGuid():N}@example.test";
        var registration = await database.Services
            .GetRequiredService<ICadastrarCliente>()
            .ExecuteAsync(CreateRegistrationInput(email));

        if (!registration.Succeeded || registration.UserId is null)
        {
            throw new Xunit.Sdk.XunitException("Client registration failed in the SQL integration test.");
        }

        var userId = registration.UserId.Value;
        var store = database.Services.GetRequiredService<IEmailOutboxStore>();
        await AssertRegistrationAndPendingOutboxAsync(database, userId, email);

        var claim = Assert.Single(await store.ClaimDueAsync(
            EmailOutboxMessageType.EmailConfirmation,
            batchSize: 1,
            TimeSpan.FromMinutes(2)));
        if (claim.UserId != userId || claim.Attempts != 1)
        {
            throw new Xunit.Sdk.XunitException("The SQL outbox claim did not match the registered user.");
        }

        var handler = new CapturingResendHandler(resendStatusCode);
        using var httpClient = new HttpClient(handler);
        var sender = new ResendEmailSender(
            httpClient,
            Options.Create(new ResendEmailOptions
            {
                ApiKey = SyntheticApiKey,
                FromEmail = SenderAddress
            }));
        var composer = new EmailConfirmationMessageComposer(
            Options.Create(new EmailConfirmationEmailOptions
            {
                PublicFrontendBaseUrl = FrontendBaseUrl
            }),
            new TestHostEnvironment());
        var processor = new EmailConfirmationOutboxProcessor(
            store,
            database.Services.GetRequiredService<IEmailConfirmationService>(),
            composer,
            sender,
            database.Services.GetRequiredService<TimeProvider>());

        await processor.ProcessAsync(claim);

        var payload = ReadPayload(handler);
        AssertSafeEqual(SenderAddress, payload.From, "sender address");
        AssertSafeEqual(email, payload.Recipient, "recipient");
        AssertSafeEqual("Confirme seu e-mail", payload.Subject, "subject");
        AssertSafeEqual(HttpMethod.Post, handler.Method, "HTTP method");
        AssertSafeEqual(
            new Uri("https://api.resend.com/emails"),
            handler.RequestUri,
            "Resend endpoint");
        AssertSafeEqual("Bearer", handler.AuthorizationScheme, "authorization scheme");
        AssertSafeEqual(SyntheticApiKey, handler.AuthorizationParameter, "authorization token");

        var (link, token) = ValidateConfirmationHtml(payload.Html, email);

        await using var verification = database.CreateVerificationContext();
        var user = await verification.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
        var outboxMessage = await verification.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message => message.UserId == userId);

        if (user.EmailConfirmed)
        {
            throw new Xunit.Sdk.XunitException("Sending the confirmation email unexpectedly confirmed the user.");
        }

        Assert.Equal(expectedOutboxStatus, outboxMessage.Status);
        Assert.Equal(1, outboxMessage.Attempts);
        AssertNoSensitiveOutboxData(
            verification,
            outboxMessage,
            [email, token, link, payload.Html]);

        if (expectedOutboxStatus == EmailOutboxMessageStatus.Completed)
        {
            if (outboxMessage.CompletedAtUtc is null || outboxMessage.LeaseExpiresAtUtc is not null)
            {
                throw new Xunit.Sdk.XunitException("Successful delivery did not complete the current claim.");
            }

            Assert.Null(outboxMessage.ActiveDeduplicationKey);
        }
        else
        {
            Assert.Equal($"email-confirmation:{userId:N}", outboxMessage.ActiveDeduplicationKey);
            if (outboxMessage.CompletedAtUtc is not null || outboxMessage.LeaseExpiresAtUtc is not null)
            {
                throw new Xunit.Sdk.XunitException("Failed delivery did not release its processing lease.");
            }
        }

        if (expectedOutboxStatus == EmailOutboxMessageStatus.Pending
            && outboxMessage.NextAttemptAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new Xunit.Sdk.XunitException("Transient delivery failure did not schedule a future retry.");
        }
    }

    private static async Task AssertRegistrationAndPendingOutboxAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string email)
    {
        await using var verification = database.CreateVerificationContext();
        var user = await verification.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
        var outboxMessage = await verification.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message => message.UserId == userId);

        AssertSafeEqual(email, user.Email, "registered user email");
        if (user.EmailConfirmed)
        {
            throw new Xunit.Sdk.XunitException("Newly registered user's email was unexpectedly confirmed.");
        }

        Assert.Equal(EmailOutboxMessageType.EmailConfirmation, outboxMessage.MessageType);
        Assert.Equal(EmailOutboxMessageStatus.Pending, outboxMessage.Status);
        Assert.Equal(0, outboxMessage.Attempts);
        Assert.Equal($"email-confirmation:{userId:N}", outboxMessage.ActiveDeduplicationKey);

        if (database.RegistrationTransactionIdAtBegin is null
            || database.RegistrationTransactionIdAtBegin != database.OutboxTransactionIdAtEnqueue)
        {
            throw new Xunit.Sdk.XunitException("Registration and Outbox enqueue did not share one transaction.");
        }

        AssertNoSensitiveOutboxData(verification, outboxMessage, [email]);
    }

    private static void AssertNoSensitiveOutboxData(
        XaquiamuniDbContext dbContext,
        EmailOutboxMessage message,
        IReadOnlyCollection<string> sensitiveValues)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(EmailOutboxMessage));
        if (entityType is null)
        {
            throw new Xunit.Sdk.XunitException("The Outbox entity is missing from the SQL model.");
        }

        var table = StoreObjectIdentifier.Table(
            entityType.GetTableName()!,
            entityType.GetSchema());
        var allowedColumnNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Id",
            "UserId",
            "MessageType",
            "Status",
            "Attempts",
            "CreatedAtUtc",
            "NextAttemptAtUtc",
            "CompletedAtUtc",
            "ActiveDeduplicationKey",
            "LeaseExpiresAtUtc",
            "RowVersion"
        };
        var properties = entityType.GetProperties().ToArray();
        var columnNames = properties
            .Select(property => property.GetColumnName(table))
            .Where(columnName => columnName is not null)
            .ToHashSet(StringComparer.Ordinal);
        if (!columnNames.SetEquals(allowedColumnNames))
        {
            throw new Xunit.Sdk.XunitException("The Outbox schema contains unexpected persisted fields.");
        }

        foreach (var property in properties)
        {
            if (property.ClrType != typeof(string)
                || property.PropertyInfo?.GetValue(message) is not string storedValue)
            {
                continue;
            }

            if (sensitiveValues.Any(value =>
                    storedValue.Contains(value, StringComparison.Ordinal)))
            {
                throw new Xunit.Sdk.XunitException("Sensitive email content was persisted in the Outbox.");
            }
        }
    }

    private static (string Link, string Token) ValidateConfirmationHtml(string html, string email)
    {
        var decodedHtml = WebUtility.HtmlDecode(html);
        var hrefStart = decodedHtml.IndexOf("href=\"", StringComparison.Ordinal);
        if (hrefStart < 0)
        {
            throw new Xunit.Sdk.XunitException("The Resend payload does not contain a confirmation link.");
        }

        hrefStart += "href=\"".Length;
        var hrefEnd = decodedHtml.IndexOf('"', hrefStart);
        if (hrefEnd <= hrefStart
            || !Uri.TryCreate(decodedHtml[hrefStart..hrefEnd], UriKind.Absolute, out var linkUri)
            || linkUri.Scheme != Uri.UriSchemeHttps
            || linkUri.Host != "frontend.example.test"
            || linkUri.AbsolutePath != "/confirm-email")
        {
            throw new Xunit.Sdk.XunitException("The confirmation link in the Resend payload is invalid.");
        }

        var fragmentValues = linkUri.Fragment
            .TrimStart('#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.Ordinal);
        if (!fragmentValues.TryGetValue("email", out var linkedEmail)
            || !fragmentValues.TryGetValue("token", out var token)
            || string.IsNullOrWhiteSpace(token))
        {
            throw new Xunit.Sdk.XunitException("The confirmation link does not contain its required fragment values.");
        }

        AssertSafeEqual(email, linkedEmail, "confirmation link email");
        if (!decodedHtml.StartsWith("<p>", StringComparison.Ordinal)
            || !decodedHtml.EndsWith("</p>", StringComparison.Ordinal)
            || decodedHtml.Count(character => character == '<') != decodedHtml.Count(character => character == '>'))
        {
            throw new Xunit.Sdk.XunitException("The confirmation body is not a valid HTML fragment.");
        }

        return (decodedHtml[hrefStart..hrefEnd], token);
    }

    private static ResendPayload ReadPayload(CapturingResendHandler handler)
    {
        if (handler.RequestCount != 1 || string.IsNullOrWhiteSpace(handler.JsonBody))
        {
            throw new Xunit.Sdk.XunitException("The fake Resend handler did not receive exactly one payload.");
        }

        try
        {
            using var document = JsonDocument.Parse(handler.JsonBody);
            var root = document.RootElement;
            var recipients = root.GetProperty("to");
            if (recipients.ValueKind != JsonValueKind.Array || recipients.GetArrayLength() != 1)
            {
                throw new Xunit.Sdk.XunitException("The Resend payload has an invalid recipient list.");
            }

            return new ResendPayload(
                root.GetProperty("from").GetString()!,
                recipients[0].GetString()!,
                root.GetProperty("subject").GetString()!,
                root.GetProperty("html").GetString()!);
        }
        catch (JsonException)
        {
            throw new Xunit.Sdk.XunitException("The fake Resend handler received invalid JSON.");
        }
    }

    private static void AssertSafeEqual<T>(T expected, T? actual, string field)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Xunit.Sdk.XunitException($"Resend integration {field} did not match.");
        }
    }

    private static CadastroClienteInput CreateRegistrationInput(string email) =>
        new()
        {
            NomeCompleto = "Cliente Integração",
            Cpf = "529.982.247-25",
            Email = email,
            Telefone = "+55 (11) 91234-5678",
            Senha = "Senha de teste 987!",
            EnderecoInicial = new EnderecoInicialInput
            {
                Cep = "95000-000",
                Uf = "RS",
                Cidade = "Vacaria",
                Bairro = "Centro",
                Logradouro = "Rua das Flores",
                Numero = "10",
                Complemento = "Casa 2"
            }
        };

    private sealed record ResendPayload(
        string From,
        string Recipient,
        string Subject,
        string Html);

    private sealed class CapturingResendHandler(HttpStatusCode responseStatusCode)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? JsonBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            JsonBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(responseStatusCode);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = nameof(EmailConfirmationResendSqlServerIntegrationTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
