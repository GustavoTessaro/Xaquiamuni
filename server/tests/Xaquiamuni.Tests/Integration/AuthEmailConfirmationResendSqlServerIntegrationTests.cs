using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class AuthEmailConfirmationResendSqlServerIntegrationTests
{
    private const string GenericResponse =
        "Se houver uma conta elegível, enviaremos um e-mail de confirmação.";

    [SqlServerIntegrationFact]
    public async Task RealRegistrationAndResend_ReturnSameGenericResponseForEligibleAndIneligibleRequests()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var email = CreateEmail();
        var userId = await CreateRegisteredClientAsync(database, email);

        var initialMessages = await ReadMessagesAsync(database, userId);
        var initialMessage = Assert.Single(initialMessages);
        Assert.Equal(EmailOutboxMessageStatus.Pending, initialMessage.Status);
        Assert.Equal(0, initialMessage.Attempts);
        Assert.Equal($"email-confirmation:{userId:N}", initialMessage.ActiveDeduplicationKey);
        await using (var verification = database.CreateVerificationContext())
        {
            var user = await verification.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
            Assert.False(user.EmailConfirmed);
        }

        var immediateResponse = await SendResendAsync(client, antiforgeryToken, email);
        AssertGenericAccepted(immediateResponse);
        Assert.DoesNotContain(email, immediateResponse.Body, StringComparison.Ordinal);
        Assert.Single(await ReadMessagesAsync(database, userId));

        var missingAccountResponse = await SendResendAsync(
            client,
            antiforgeryToken,
            CreateEmail());
        AssertGenericAccepted(missingAccountResponse);
        Assert.Equal(immediateResponse.Body, missingAccountResponse.Body);
        Assert.Single(await ReadMessagesAsync(database));

        await ConfirmUserAsync(database, email);
        var confirmedResponse = await SendResendAsync(client, antiforgeryToken, email);
        AssertGenericAccepted(confirmedResponse);
        Assert.Equal(immediateResponse.Body, confirmedResponse.Body);
        Assert.Single(await ReadMessagesAsync(database, userId));
    }

    [SqlServerIntegrationFact]
    public async Task CompletedMessageOutsideCooldown_CreatesANewPendingMessage()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var email = CreateEmail();
        var userId = await CreateRegisteredClientAsync(database, email);
        var initialMessage = Assert.Single(await ReadMessagesAsync(database, userId));
        await MarkCompletedAgoAsync(database, initialMessage.Id, minutesAgo: 5);

        var response = await SendResendAsync(client, antiforgeryToken, email);

        AssertGenericAccepted(response);
        var messages = await ReadMessagesAsync(database, userId);
        Assert.Equal(2, messages.Count);
        var completed = Assert.Single(messages, message => message.Id == initialMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Completed, completed.Status);
        Assert.Null(completed.ActiveDeduplicationKey);
        var pending = Assert.Single(messages, message => message.Id != initialMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, pending.Status);
        Assert.Equal(0, pending.Attempts);
        Assert.Equal($"email-confirmation:{userId:N}", pending.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task DeadLetterResend_PreservesHistoryAndReleasesTheActiveKey()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var email = CreateEmail();
        var userId = await CreateRegisteredClientAsync(database, email);
        var initialMessage = Assert.Single(await ReadMessagesAsync(database, userId));
        await MarkDeadLetterAgoAsync(database, initialMessage.Id, userId, minutesAgo: 5);
        var deadLetterBefore = Assert.Single(await ReadMessagesAsync(database, userId));

        var response = await SendResendAsync(client, antiforgeryToken, email);

        AssertGenericAccepted(response);
        var messages = await ReadMessagesAsync(database, userId);
        Assert.Equal(2, messages.Count);
        var oldMessage = Assert.Single(messages, message => message.Id == initialMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, oldMessage.Status);
        Assert.Equal(deadLetterBefore.Attempts, oldMessage.Attempts);
        Assert.Equal(deadLetterBefore.CreatedAtUtc, oldMessage.CreatedAtUtc);
        Assert.Null(oldMessage.ActiveDeduplicationKey);
        Assert.NotEqual(deadLetterBefore.RowVersion, oldMessage.RowVersion);
        var newMessage = Assert.Single(messages, message => message.Id != initialMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, newMessage.Status);
        Assert.Equal(0, newMessage.Attempts);
        Assert.Equal($"email-confirmation:{userId:N}", newMessage.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task FiveRequestsInRollingHour_IncludingRegistrationPreventAnotherEnqueue()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var email = CreateEmail();
        var userId = await CreateRegisteredClientAsync(database, email);

        for (var requestNumber = 2; requestNumber <= 5; requestNumber++)
        {
            var pending = Assert.Single(
                await ReadMessagesAsync(database, userId),
                message => message.Status == EmailOutboxMessageStatus.Pending);
            await MarkCompletedAgoAsync(database, pending.Id, minutesAgo: 3);

            var accepted = await SendResendAsync(client, antiforgeryToken, email);
            AssertGenericAccepted(accepted);
        }

        var beforeLimitedRequest = await ReadMessagesAsync(database, userId);
        Assert.Equal(5, beforeLimitedRequest.Count);
        var lastPending = Assert.Single(
            beforeLimitedRequest,
            message => message.Status == EmailOutboxMessageStatus.Pending);
        await MarkCompletedAgoAsync(database, lastPending.Id, minutesAgo: 3);

        var limitedResponse = await SendResendAsync(client, antiforgeryToken, email);

        AssertGenericAccepted(limitedResponse);
        var afterLimitedRequest = await ReadMessagesAsync(database, userId);
        Assert.Equal(5, afterLimitedRequest.Count);
        Assert.All(afterLimitedRequest, message => Assert.Equal(EmailOutboxMessageStatus.Completed, message.Status));
        Assert.Equal(GenericResponse, GetPublicMessage(limitedResponse.Body));
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentEligibleRequests_CreateOnlyOnePendingMessage()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await database.EnableReadCommittedSnapshotAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var email = CreateEmail();
        var userId = await CreateRegisteredClientAsync(database, email);
        var initialMessage = Assert.Single(await ReadMessagesAsync(database, userId));
        await MarkCompletedAgoAsync(database, initialMessage.Id, minutesAgo: 5);

        using var firstRequest = CreateResendRequest(antiforgeryToken, email);
        using var secondRequest = CreateResendRequest(antiforgeryToken, email);
        var responses = await Task.WhenAll(
            client.SendAsync(firstRequest),
            client.SendAsync(secondRequest));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        AssertNoStore(firstResponse);
        AssertNoStore(secondResponse);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(GenericResponse, GetPublicMessage(firstBody));
        var messages = await ReadMessagesAsync(database, userId);
        Assert.Equal(2, messages.Count);
        Assert.Single(messages, message => message.Status == EmailOutboxMessageStatus.Pending);
        Assert.Single(
            messages,
            message => message.ActiveDeduplicationKey == $"email-confirmation:{userId:N}");
    }

    [SqlServerIntegrationFact]
    public async Task MissingAntiforgeryToken_ReturnsBadRequestWithoutChangingOutbox()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var email = CreateEmail();
        var userId = await CreateRegisteredClientAsync(database, email);
        var before = Assert.Single(await ReadMessagesAsync(database, userId));
        using var request = CreateResendRequest(requestToken: null, email: email);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        var after = Assert.Single(await ReadMessagesAsync(database, userId));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.ActiveDeduplicationKey, after.ActiveDeduplicationKey);
    }

    private static WebApplicationFactory<Program> CreateFactory(SqlServerTestDatabase database)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");
        var encryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var fingerprintKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString,
                    ["CpfProtection:CurrentEncryptionKeyVersion"] = "enc-integration",
                    ["CpfProtection:EncryptionKeys:enc-integration"] = encryptionKey,
                    ["CpfProtection:CurrentFingerprintKeyVersion"] = "fp-integration",
                    ["CpfProtection:FingerprintKeys:fp-integration"] = fingerprintKey,
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<XaquiamuniDbContext>(
                    options => options.UseSqlServer(connectionString));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        });

        using var scope = factory.Services.CreateScope();
        var configuredConnectionString = scope.ServiceProvider
            .GetRequiredService<XaquiamuniDbContext>()
            .Database.GetConnectionString();
        var expectedConnection = new SqlConnectionStringBuilder(connectionString);
        var configuredConnection = new SqlConnectionStringBuilder(configuredConnectionString);
        if (!string.Equals(configuredConnection.DataSource, @".\SQLEXPRESS", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(configuredConnection.InitialCatalog, expectedConnection.InitialCatalog, StringComparison.Ordinal)
            || !configuredConnection.InitialCatalog.StartsWith(
                "Xaquiamuni_IntegrationTest_",
                StringComparison.Ordinal))
        {
            factory.Dispose();
            throw new InvalidOperationException(
                "The HTTP test host is not configured for its isolated local SQL test database.");
        }

        return factory;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        return Assert.IsType<string>(body?.RequestToken);
    }

    private static async Task<Guid> CreateRegisteredClientAsync(
        SqlServerTestDatabase database,
        string email)
    {
        var result = await database.Services
            .GetRequiredService<ICadastrarCliente>()
            .ExecuteAsync(new CadastroClienteInput
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
        });
        Assert.True(result.Succeeded);
        return Assert.IsType<Guid>(result.UserId);
    }

    private static async Task<PublicResponse> SendResendAsync(
        HttpClient client,
        string antiforgeryToken,
        string email)
    {
        using var request = CreateResendRequest(antiforgeryToken, email);
        using var response = await client.SendAsync(request);
        return new PublicResponse(response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.CacheControl?.ToString());
    }

    private static HttpRequestMessage CreateResendRequest(string? requestToken, string email)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            AuthEndpoints.ResendConfirmationEmailEndpoint)
        {
            Content = JsonContent.Create(new { email })
        };
        if (requestToken is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);
        }

        return request;
    }

    private static void AssertGenericAccepted(PublicResponse response)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(response.CacheControl?.Contains(
            "no-store",
            StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(GenericResponse, GetPublicMessage(response.Body));
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);

    private static string GetPublicMessage(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);
        return document.RootElement.GetProperty("message").GetString()!;
    }

    private static async Task ConfirmUserAsync(SqlServerTestDatabase database, string email)
    {
        var userManager = database.Services.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var result = await userManager.ConfirmEmailAsync(user, token);
        Assert.True(result.Succeeded);
    }

    private static async Task<List<EmailOutboxMessage>> ReadMessagesAsync(
        SqlServerTestDatabase database,
        Guid? userId = null)
    {
        await using var context = database.CreateVerificationContext();
        var messages = context.EmailOutboxMessages.AsNoTracking();
        if (userId is Guid id)
        {
            messages = messages.Where(message => message.UserId == id);
        }

        return await messages.OrderBy(message => message.CreatedAtUtc).ToListAsync();
    }

    private static async Task MarkCompletedAgoAsync(
        SqlServerTestDatabase database,
        Guid messageId,
        int minutesAgo)
    {
        await using var context = database.CreateVerificationContext();
        var rows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @CompletedAtUtc datetimeoffset(7) =
                DATEADD(MINUTE, -{minutesAgo}, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));

            UPDATE [dbo].[EmailOutbox]
            SET [Status] = {(short)EmailOutboxMessageStatus.Completed},
                [Attempts] = CASE WHEN [Attempts] = 0 THEN 1 ELSE [Attempts] END,
                [CreatedAtUtc] = @CompletedAtUtc,
                [NextAttemptAtUtc] = @CompletedAtUtc,
                [CompletedAtUtc] = @CompletedAtUtc,
                [ActiveDeduplicationKey] = NULL,
                [LeaseExpiresAtUtc] = NULL
            WHERE [Id] = {messageId};
            """);
        Assert.Equal(1, rows);
    }

    private static async Task MarkDeadLetterAgoAsync(
        SqlServerTestDatabase database,
        Guid messageId,
        Guid userId,
        int minutesAgo)
    {
        await using var context = database.CreateVerificationContext();
        var key = $"email-confirmation:{userId:N}";
        var rows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @CreatedAtUtc datetimeoffset(7) =
                DATEADD(MINUTE, -{minutesAgo}, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));

            UPDATE [dbo].[EmailOutbox]
            SET [Status] = {(short)EmailOutboxMessageStatus.DeadLetter},
                [Attempts] = {EmailOutboxRetryPolicy.MaximumAttempts},
                [CreatedAtUtc] = @CreatedAtUtc,
                [NextAttemptAtUtc] = @CreatedAtUtc,
                [CompletedAtUtc] = NULL,
                [ActiveDeduplicationKey] = {key},
                [LeaseExpiresAtUtc] = NULL
            WHERE [Id] = {messageId};
            """);
        Assert.Equal(1, rows);
    }

    private static string CreateEmail() => $"resend-endpoint-{Guid.NewGuid():N}@example.test";

    private sealed record AntiforgeryTokenResponse(string RequestToken);

    private sealed record PublicResponse(
        HttpStatusCode StatusCode,
        string Body,
        string? CacheControl);
}
