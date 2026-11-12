using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
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
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class PasswordResetResendSqlServerIntegrationTests
{
    private const string FrontendBaseUrl = "https://frontend.example.test";
    private const string SenderAddress = "no-reply@example.test";
    private const string SyntheticApiKey = "re_sql_password_reset_test_key";
    private const string SyntheticProviderError = "synthetic-provider-error-not-for-storage";
    private const string PasswordResetPurpose = "ResetPassword";

    [SqlServerIntegrationFact]
    public async Task ScenarioA_SuccessfulDeliveryCompletesOutboxAndTokenIsValidForCurrentUser()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var client = await RegisterAndConfirmClientAsync(database);
        var passwordHashBefore = await ReadPasswordHashAsync(database, client.UserId);
        await RequestPasswordResetAsync(database, client.Email);

        var pending = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.Pending, pending.Status);
        Assert.Equal(0, pending.Attempts);
        Assert.Equal($"password-reset:{client.UserId:N}", pending.ActiveDeduplicationKey);
        var originalFingerprint = pending.CredentialGenerationFingerprint!;
        Assert.Equal(32, originalFingerprint.Length);
        var securityStamp = await ReadSecurityStampAsync(database, client.UserId);
        Assert.True(database.Services
            .GetRequiredService<ICredentialGenerationFingerprint>()
            .Matches(securityStamp, originalFingerprint));

        using var handler = new RecordingResendHandler(HttpStatusCode.Accepted);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var scope = await CreateScopeAsync(database);
        var services = scope.ServiceProvider;
        var claim = await ClaimSingleAsync(services, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(1, claim.Attempts);
        AssertEqualFingerprint(originalFingerprint, claim.CredentialGenerationFingerprint!.Value.ToArray());
        var processor = CreatePasswordResetProcessor(services, CreateSender(httpClient));

        await processor.ProcessAsync(claim);

        Assert.Equal(1, handler.RequestCount);
        var payload = handler.ReadPayload(0);
        AssertSafeEqual(SenderAddress, payload.From, "sender address");
        AssertSafeEqual(client.Email, GetOnlyRecipient(payload), "recipient");
        AssertSafeEqual("Redefinição de senha — Xaquiamuni", payload.Subject, "subject");
        AssertSafeEqual(HttpMethod.Post, handler.Method, "HTTP method");
        AssertSafeEqual(new Uri("https://api.resend.com/emails"), handler.RequestUri, "Resend endpoint");
        AssertSafeEqual("Bearer", handler.AuthorizationScheme, "authorization scheme");
        AssertSafeEqual(SyntheticApiKey, handler.AuthorizationParameter, "authorization credential");

        var resetLink = ValidatePasswordResetHtml(payload.Html, client.Email);
        AssertTrue(payload.Html.Contains("Redefinir minha senha", StringComparison.Ordinal), "reset button is missing");
        AssertTrue(payload.Html.Contains("1 hora", StringComparison.Ordinal), "token lifetime is missing");
        AssertTrue(payload.Html.Contains("ignore este e-mail", StringComparison.Ordinal), "safety guidance is missing");

        var tokenIsValid = await VerifyPasswordResetTokenAsync(
            database,
            client.UserId,
            resetLink.Token);
        Assert.True(tokenIsValid);

        var completed = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Attempts);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Null(completed.LeaseExpiresAtUtc);
        Assert.Null(completed.ActiveDeduplicationKey);
        AssertEqualFingerprint(originalFingerprint, completed.CredentialGenerationFingerprint);
        AssertSafeEqual(passwordHashBefore, await ReadPasswordHashAsync(database, client.UserId), "password hash");
        AssertNoSensitiveOutboxData(
            database,
            completed,
            [client.Email, resetLink.Token, resetLink.Link, payload.Html, SyntheticProviderError]);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioB_TransientResendFailureRetriesAndThenCompletes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var client = await RegisterAndConfirmClientAsync(database);
        await RequestPasswordResetAsync(database, client.Email);
        var initial = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        var originalFingerprint = initial.CredentialGenerationFingerprint!;
        var originalDeduplicationKey = initial.ActiveDeduplicationKey;

        using var handler = new RecordingResendHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.Accepted);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var scope = await CreateScopeAsync(database);
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IEmailOutboxStore>();
        var processor = CreatePasswordResetProcessor(services, CreateSender(httpClient));

        var firstClaim = await ClaimSingleAsync(services, EmailOutboxMessageType.PasswordReset);
        await processor.ProcessAsync(firstClaim);
        Assert.Equal(1, handler.RequestCount);

        var retry = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.Pending, retry.Status);
        Assert.Equal(1, retry.Attempts);
        Assert.True(retry.NextAttemptAtUtc > DateTimeOffset.UtcNow);
        Assert.Null(retry.LeaseExpiresAtUtc);
        AssertSafeEqual(originalDeduplicationKey, retry.ActiveDeduplicationKey, "deduplication key after retry");
        AssertEqualFingerprint(originalFingerprint, retry.CredentialGenerationFingerprint);
        AssertNoSensitiveOutboxData(database, retry, [client.Email, SyntheticProviderError]);

        await MakeRetryEligibleAsync(database, retry.Id);
        var secondClaim = await ClaimSingleAsync(services, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(firstClaim.Id, secondClaim.Id);
        Assert.Equal(2, secondClaim.Attempts);
        await processor.ProcessAsync(secondClaim);

        Assert.Equal(2, handler.RequestCount);
        var completed = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.Completed, completed.Status);
        Assert.Equal(2, completed.Attempts);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Null(completed.LeaseExpiresAtUtc);
        Assert.Null(completed.ActiveDeduplicationKey);
        AssertEqualFingerprint(originalFingerprint, completed.CredentialGenerationFingerprint);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioC_PermanentResendFailureDeadLettersWithoutPersistingProviderBody()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var client = await RegisterAndConfirmClientAsync(database);
        await RequestPasswordResetAsync(database, client.Email);
        var initial = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        var originalFingerprint = initial.CredentialGenerationFingerprint!;
        var originalDeduplicationKey = initial.ActiveDeduplicationKey;

        using var handler = new RecordingResendHandler(
            HttpStatusCode.BadRequest,
            responseBody: SyntheticProviderError);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var scope = await CreateScopeAsync(database);
        var services = scope.ServiceProvider;
        var claim = await ClaimSingleAsync(services, EmailOutboxMessageType.PasswordReset);
        await CreatePasswordResetProcessor(services, CreateSender(httpClient)).ProcessAsync(claim);

        Assert.Equal(1, handler.RequestCount);
        var deadLetter = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, deadLetter.Status);
        Assert.Equal(1, deadLetter.Attempts);
        Assert.Null(deadLetter.LeaseExpiresAtUtc);
        AssertSafeEqual(originalDeduplicationKey, deadLetter.ActiveDeduplicationKey, "DeadLetter deduplication key");
        AssertEqualFingerprint(originalFingerprint, deadLetter.CredentialGenerationFingerprint);
        AssertNoSensitiveOutboxData(
            database,
            deadLetter,
            [client.Email, SyntheticProviderError, handler.ResponseBody]);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioD_OldCredentialGenerationCancelsWithoutSending()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var client = await RegisterAndConfirmClientAsync(database);
        await RequestPasswordResetAsync(database, client.Email);
        var initial = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        var historicalFingerprint = initial.CredentialGenerationFingerprint!;
        var historicalKey = initial.ActiveDeduplicationKey;

        var newStamp = await UpdateSecurityStampAsync(database, client.UserId);
        var newFingerprint = database.Services
            .GetRequiredService<ICredentialGenerationFingerprint>()
            .Compute(newStamp);
        Assert.False(newFingerprint.AsSpan().SequenceEqual(historicalFingerprint));

        using var handler = new RecordingResendHandler(HttpStatusCode.Accepted);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var scope = await CreateScopeAsync(database);
        var services = scope.ServiceProvider;
        var claim = await ClaimSingleAsync(services, EmailOutboxMessageType.PasswordReset);
        await CreatePasswordResetProcessor(services, CreateSender(httpClient)).ProcessAsync(claim);

        Assert.Equal(0, handler.RequestCount);
        var cancelled = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.Attempts);
        Assert.Null(cancelled.LeaseExpiresAtUtc);
        Assert.Null(cancelled.ActiveDeduplicationKey);
        AssertSafeEqual(historicalKey, initial.ActiveDeduplicationKey, "historical deduplication key");
        AssertEqualFingerprint(historicalFingerprint, cancelled.CredentialGenerationFingerprint);
        AssertNoSensitiveOutboxData(database, cancelled, [client.Email]);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioE_ConcurrentStampChangeKeepsTokenBoundToValidatedUserSnapshot()
    {
        using var dataProtectionServices = CreateEphemeralDataProtectionServiceProvider();
        var protectionGate = new PasswordResetProtectionGate();
        var synchronizedProvider = new SynchronizedDataProtectionProvider(
            dataProtectionServices.GetRequiredService<IDataProtectionProvider>(),
            protectionGate);
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dataProtectionProvider: synchronizedProvider);
        var client = await RegisterAndConfirmClientAsync(database);
        await RequestPasswordResetAsync(database, client.Email);
        var originalFingerprint = (await ReadMessageAsync(
            database,
            client.UserId,
            EmailOutboxMessageType.PasswordReset)).CredentialGenerationFingerprint!;
        var stampA = await ReadSecurityStampAsync(database, client.UserId);

        using var handler = new RecordingResendHandler(HttpStatusCode.Accepted);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var processingScope = await CreateScopeAsync(database);
        var services = processingScope.ServiceProvider;
        var claim = await ClaimSingleAsync(services, EmailOutboxMessageType.PasswordReset);
        var processTask = Task.Run(() =>
            CreatePasswordResetProcessor(services, CreateSender(httpClient)).ProcessAsync(claim));

        try
        {
            await protectionGate.ProtectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var stampB = await UpdateSecurityStampAsync(database, client.UserId);
            AssertSafeDifferent(stampA, stampB, "security stamp generation");
        }
        finally
        {
            protectionGate.AllowProtection();
        }

        await processTask;
        Assert.Equal(1, handler.RequestCount);
        var payload = handler.ReadPayload(0);
        var resetLink = ValidatePasswordResetHtml(payload.Html, client.Email);
        AssertEqualFingerprint(originalFingerprint,
            (await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset))
            .CredentialGenerationFingerprint);

        var userWithCurrentStamp = await ReadUserAsync(database, client.UserId);
        var snapshotA = new XaquiamuniUser
        {
            Id = client.UserId,
            UserName = client.Email,
            Email = client.Email,
            SecurityStamp = stampA
        };
        var tokenValidForSnapshotA = await VerifyPasswordResetTokenAsync(database, snapshotA, resetLink.Token);
        var tokenValidForGenerationB = await VerifyPasswordResetTokenAsync(
            database,
            userWithCurrentStamp,
            resetLink.Token);
        Assert.True(tokenValidForSnapshotA);
        Assert.False(tokenValidForGenerationB);
        AssertNoSensitiveOutboxData(
            database,
            await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset),
            [client.Email, resetLink.Token, resetLink.Link, payload.Html]);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioF_DispatcherProcessesBothTypesIndependentlyInOneDatabase()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var confirmationClient = await RegisterUnconfirmedClientAsync(database);
        var resetEmail = $"password-reset-dispatch-{Guid.NewGuid():N}@example.test";
        var resetUserId = await CreateConfirmedIdentityUserAsync(database, resetEmail);
        await RequestPasswordResetAsync(database, resetEmail);

        var confirmationPending = await ReadMessageAsync(
            database,
            confirmationClient.UserId,
            EmailOutboxMessageType.EmailConfirmation);
        var resetPending = await ReadMessageAsync(
            database,
            resetUserId,
            EmailOutboxMessageType.PasswordReset);
        Assert.Null(confirmationPending.CredentialGenerationFingerprint);
        Assert.NotNull(resetPending.CredentialGenerationFingerprint);
        AssertSafeDifferent(
            confirmationPending.ActiveDeduplicationKey,
            resetPending.ActiveDeduplicationKey,
            "message deduplication keys");

        using var handler = new RecordingResendHandler(
            HttpStatusCode.Accepted,
            HttpStatusCode.Accepted);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var scope = await CreateScopeAsync(database);
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IEmailOutboxStore>();
        var sender = CreateSender(httpClient);
        var confirmationProcessor = CreateEmailConfirmationProcessor(services, sender);
        var passwordResetProcessor = CreatePasswordResetProcessor(services, sender);
        var dispatcher = new EmailOutboxDispatcher(
            [confirmationProcessor, passwordResetProcessor]);

        var confirmationClaim = await ClaimSingleAsync(services, EmailOutboxMessageType.EmailConfirmation);
        var passwordResetClaim = await ClaimSingleAsync(services, EmailOutboxMessageType.PasswordReset);
        await dispatcher.ProcessAsync(confirmationClaim);
        await dispatcher.ProcessAsync(passwordResetClaim);

        Assert.Equal(2, handler.RequestCount);
        var confirmationPayload = handler.ReadPayload(0);
        var resetPayload = handler.ReadPayload(1);
        AssertSafeEqual(confirmationClient.Email, GetOnlyRecipient(confirmationPayload), "confirmation recipient");
        AssertSafeEqual(resetEmail, GetOnlyRecipient(resetPayload), "reset recipient");
        AssertSafeEqual("Confirme seu e-mail", confirmationPayload.Subject, "confirmation subject");
        AssertSafeEqual("Redefinição de senha — Xaquiamuni", resetPayload.Subject, "reset subject");
        ValidateConfirmationHtml(confirmationPayload.Html, confirmationClient.Email);
        var resetLink = ValidatePasswordResetHtml(resetPayload.Html, resetEmail);

        var confirmationCompleted = await ReadMessageAsync(
            database,
            confirmationClient.UserId,
            EmailOutboxMessageType.EmailConfirmation);
        var resetCompleted = await ReadMessageAsync(
            database,
            resetUserId,
            EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.Completed, confirmationCompleted.Status);
        Assert.Equal(EmailOutboxMessageStatus.Completed, resetCompleted.Status);
        Assert.Equal(1, confirmationCompleted.Attempts);
        Assert.Equal(1, resetCompleted.Attempts);
        Assert.Null(confirmationCompleted.CredentialGenerationFingerprint);
        Assert.NotNull(resetCompleted.CredentialGenerationFingerprint);
        Assert.Null(confirmationCompleted.ActiveDeduplicationKey);
        Assert.Null(resetCompleted.ActiveDeduplicationKey);
        Assert.False((await ReadUserAsync(database, confirmationClient.UserId)).EmailConfirmed);
        Assert.True(await VerifyPasswordResetTokenAsync(database, resetUserId, resetLink.Token));
        Assert.True(dispatcher.SupportedMessageTypes.SequenceEqual(
            [EmailOutboxMessageType.EmailConfirmation, EmailOutboxMessageType.PasswordReset]));
        AssertNoSensitiveOutboxData(
            database,
            confirmationCompleted,
            [confirmationClient.Email, confirmationPayload.Html]);
        AssertNoSensitiveOutboxData(
            database,
            resetCompleted,
            [resetEmail, resetLink.Token, resetLink.Link, resetPayload.Html]);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioG_ExpiredLeaseAndNewClaimPreventStaleCompletion()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var client = await RegisterAndConfirmClientAsync(database);
        await RequestPasswordResetAsync(database, client.Email);

        using var handler = new RecordingResendHandler(
            HttpStatusCode.Accepted,
            pauseBeforeResponse: true);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        await using var firstScope = await CreateScopeAsync(database);
        var firstServices = firstScope.ServiceProvider;
        var oldClaim = await ClaimSingleAsync(firstServices, EmailOutboxMessageType.PasswordReset);
        var oldProcessor = CreatePasswordResetProcessor(firstServices, CreateSender(httpClient));
        var oldProcessTask = Task.Run(() => oldProcessor.ProcessAsync(oldClaim));

        await handler.ResponsePaused.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await ExpireLeaseAsync(database, oldClaim.Id);

        await using var secondScope = await CreateScopeAsync(database);
        var secondServices = secondScope.ServiceProvider;
        var newClaim = await ClaimSingleAsync(secondServices, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(oldClaim.Id, newClaim.Id);
        Assert.Equal(oldClaim.Attempts + 1, newClaim.Attempts);
        AssertSafeDifferent(
            oldClaim.RowVersion.ToArray(),
            newClaim.RowVersion.ToArray(),
            "claim row versions");

        handler.AllowResponse();
        await oldProcessTask;

        Assert.Equal(1, handler.RequestCount);
        var persisted = await ReadMessageAsync(database, client.UserId, EmailOutboxMessageType.PasswordReset);
        Assert.Equal(EmailOutboxMessageStatus.Processing, persisted.Status);
        Assert.Equal(newClaim.Attempts, persisted.Attempts);
        Assert.Null(persisted.CompletedAtUtc);
        AssertSafeEqual(newClaim.LeaseExpiresAtUtc, persisted.LeaseExpiresAtUtc, "new owner's lease");
        AssertSafeEqual(
            $"password-reset:{client.UserId:N}",
            persisted.ActiveDeduplicationKey,
            "new owner's deduplication key");
        Assert.True(persisted.RowVersion.SequenceEqual(newClaim.RowVersion.ToArray()));
        Assert.True(persisted.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    private static async Task<ClientAccount> RegisterAndConfirmClientAsync(SqlServerTestDatabase database)
    {
        var client = await RegisterUnconfirmedClientAsync(database);
        var confirmationService = database.Services.GetRequiredService<IEmailConfirmationService>();
        var generatedToken = await confirmationService.GenerateEmailConfirmationTokenForUserAsync(client.UserId);
        Assert.Equal(EmailConfirmationTokenStatus.Generated, generatedToken.Status);
        AssertSafeTrue(!string.IsNullOrWhiteSpace(generatedToken.Token), "Identity did not generate the confirmation token.");
        var confirmationResult = await confirmationService.ConfirmEmailAsync(client.Email, generatedToken.Token!);
        Assert.Equal(EmailConfirmationStatus.Confirmed, confirmationResult.Status);
        Assert.True((await ReadUserAsync(database, client.UserId)).EmailConfirmed);
        return client;
    }

    private static async Task<ClientAccount> RegisterUnconfirmedClientAsync(SqlServerTestDatabase database)
    {
        var email = $"password-reset-integration-{Guid.NewGuid():N}@example.test";
        var registration = await database.Services
            .GetRequiredService<ICadastrarCliente>()
            .ExecuteAsync(CreateRegistrationInput(email));
        if (!registration.Succeeded || registration.UserId is null)
        {
            throw new Xunit.Sdk.XunitException("Client registration failed in the SQL integration test.");
        }

        return new ClientAccount(registration.UserId.Value, email);
    }

    private static async Task<Guid> CreateConfirmedIdentityUserAsync(
        SqlServerTestDatabase database,
        string email)
    {
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var result = await database.Services
            .GetRequiredService<UserManager<XaquiamuniUser>>()
            .CreateAsync(user, "Password Reset Integration 987!");
        if (!result.Succeeded)
        {
            throw new Xunit.Sdk.XunitException("Confirmed Identity user creation failed in the SQL integration test.");
        }

        return user.Id;
    }

    private static async Task RequestPasswordResetAsync(SqlServerTestDatabase database, string email)
    {
        var result = await database.Services
            .GetRequiredService<ISolicitarRecuperacaoSenha>()
            .ExecuteAsync(email);
        Assert.Equal(SolicitarRecuperacaoSenhaStatus.RequestProcessed, result.Status);
    }

    private static async Task<EmailOutboxMessage> ReadMessageAsync(
        SqlServerTestDatabase database,
        Guid userId,
        EmailOutboxMessageType messageType)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.UserId == userId && message.MessageType == messageType);
    }

    private static async Task<XaquiamuniUser> ReadUserAsync(SqlServerTestDatabase database, Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        return await context.Users.AsNoTracking().SingleAsync(user => user.Id == userId);
    }

    private static async Task<string> ReadSecurityStampAsync(SqlServerTestDatabase database, Guid userId) =>
        (await ReadUserAsync(database, userId)).SecurityStamp!;

    private static async Task<string?> ReadPasswordHashAsync(SqlServerTestDatabase database, Guid userId) =>
        (await ReadUserAsync(database, userId)).PasswordHash;

    private static async Task<string> UpdateSecurityStampAsync(SqlServerTestDatabase database, Guid userId)
    {
        await using var scope = await CreateScopeAsync(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var result = await userManager.UpdateSecurityStampAsync(user);
        Assert.True(result.Succeeded);
        return user.SecurityStamp!;
    }

    private static async Task MakeRetryEligibleAsync(SqlServerTestDatabase database, Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[EmailOutbox]
            SET [NextAttemptAtUtc] =
                DATEADD(second, -1, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'))
            WHERE [Id] = {messageId};
            """);
    }

    private static async Task ExpireLeaseAsync(SqlServerTestDatabase database, Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[EmailOutbox]
            SET [LeaseExpiresAtUtc] =
                DATEADD(second, -1, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'))
            WHERE [Id] = {messageId}
                AND [Status] = {(short)EmailOutboxMessageStatus.Processing};
            """);
    }

    private static async Task<EmailOutboxClaim> ClaimSingleAsync(
        IServiceProvider services,
        EmailOutboxMessageType messageType) =>
        Assert.Single(await services.GetRequiredService<IEmailOutboxStore>().ClaimDueAsync(
            messageType,
            batchSize: 1,
            TimeSpan.FromMinutes(2)));

    private static Task<AsyncServiceScope> CreateScopeAsync(SqlServerTestDatabase database) =>
        Task.FromResult(
            database.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope());

    private static PasswordResetOutboxProcessor CreatePasswordResetProcessor(
        IServiceProvider services,
        IEmailSender emailSender) =>
        new(
            services.GetRequiredService<IEmailOutboxStore>(),
            services.GetRequiredService<IPasswordResetService>(),
            CreatePasswordResetComposer(),
            emailSender,
            services.GetRequiredService<TimeProvider>());

    private static EmailConfirmationOutboxProcessor CreateEmailConfirmationProcessor(
        IServiceProvider services,
        IEmailSender emailSender) =>
        new(
            services.GetRequiredService<IEmailOutboxStore>(),
            services.GetRequiredService<IEmailConfirmationService>(),
            new EmailConfirmationMessageComposer(
                Options.Create(new EmailConfirmationEmailOptions
                {
                    PublicFrontendBaseUrl = FrontendBaseUrl
                }),
                new TestHostEnvironment()),
            emailSender,
            services.GetRequiredService<TimeProvider>());

    private static PasswordResetMessageComposer CreatePasswordResetComposer() =>
        new(
            Options.Create(new EmailConfirmationEmailOptions
            {
                PublicFrontendBaseUrl = FrontendBaseUrl
            }),
            new TestHostEnvironment());

    private static ResendEmailSender CreateSender(HttpClient httpClient) =>
        new(
            httpClient,
            Options.Create(new ResendEmailOptions
            {
                ApiKey = SyntheticApiKey,
                FromEmail = SenderAddress
            }));

    private static async Task<bool> VerifyPasswordResetTokenAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string token)
    {
        await using var scope = await CreateScopeAsync(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await VerifyPasswordResetTokenAsync(userManager, user, token);
    }

    private static async Task<bool> VerifyPasswordResetTokenAsync(
        SqlServerTestDatabase database,
        XaquiamuniUser userSnapshot,
        string token)
    {
        await using var scope = await CreateScopeAsync(database);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        return await VerifyPasswordResetTokenAsync(userManager, userSnapshot, token);
    }

    private static Task<bool> VerifyPasswordResetTokenAsync(
        UserManager<XaquiamuniUser> userManager,
        XaquiamuniUser user,
        string token) =>
        userManager.VerifyUserTokenAsync(
            user,
            XaquiamuniPasswordResetTokenProvider.ProviderName,
            PasswordResetPurpose,
            token);

    private static ResetLink ValidatePasswordResetHtml(string html, string expectedEmail)
    {
        var decodedHtml = WebUtility.HtmlDecode(html);
        var hrefStart = decodedHtml.IndexOf("href=\"", StringComparison.Ordinal);
        if (hrefStart < 0)
        {
            throw new Xunit.Sdk.XunitException("The Resend payload does not contain a reset link.");
        }

        hrefStart += "href=\"".Length;
        var hrefEnd = decodedHtml.IndexOf('"', hrefStart);
        if (hrefEnd <= hrefStart
            || !Uri.TryCreate(decodedHtml[hrefStart..hrefEnd], UriKind.Absolute, out var linkUri)
            || linkUri.Scheme != Uri.UriSchemeHttps
            || linkUri.Host != "frontend.example.test"
            || linkUri.AbsolutePath != "/reset-password"
            || !string.IsNullOrEmpty(linkUri.Query))
        {
            throw new Xunit.Sdk.XunitException("The password reset link in the Resend payload is invalid.");
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
            throw new Xunit.Sdk.XunitException("The reset link does not contain its required fragment values.");
        }

        AssertSafeEqual(expectedEmail, linkedEmail, "fragment email");
        AssertTrue(decodedHtml.Contains("Redefinir minha senha", StringComparison.Ordinal), "reset button is missing");
        AssertTrue(decodedHtml.Contains("1 hora", StringComparison.Ordinal), "token lifetime is missing");
        AssertTrue(decodedHtml.Contains("ignore este e-mail", StringComparison.Ordinal), "safety guidance is missing");
        return new ResetLink(decodedHtml[hrefStart..hrefEnd], linkedEmail, token);
    }

    private static void ValidateConfirmationHtml(string html, string expectedEmail)
    {
        var decodedHtml = WebUtility.HtmlDecode(html);
        var hrefStart = decodedHtml.IndexOf("href=\"", StringComparison.Ordinal);
        if (hrefStart < 0)
        {
            throw new Xunit.Sdk.XunitException("The confirmation payload does not contain a link.");
        }

        hrefStart += "href=\"".Length;
        var hrefEnd = decodedHtml.IndexOf('"', hrefStart);
        if (hrefEnd <= hrefStart
            || !Uri.TryCreate(decodedHtml[hrefStart..hrefEnd], UriKind.Absolute, out var linkUri)
            || linkUri.Scheme != Uri.UriSchemeHttps
            || linkUri.Host != "frontend.example.test"
            || linkUri.AbsolutePath != "/confirm-email"
            || !string.IsNullOrEmpty(linkUri.Query))
        {
            throw new Xunit.Sdk.XunitException("The email confirmation link in the Resend payload is invalid.");
        }

        var values = ParseFragment(linkUri);
        AssertSafeEqual(expectedEmail, values["email"], "confirmation link email");
        AssertTrue(!string.IsNullOrWhiteSpace(values["token"]), "confirmation token is missing");
    }

    private static Dictionary<string, string> ParseFragment(Uri uri) =>
        uri.Fragment.TrimStart('#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.Ordinal);

    private static void AssertNoSensitiveOutboxData(
        SqlServerTestDatabase database,
        EmailOutboxMessage message,
        IReadOnlyCollection<string?> sensitiveValues)
    {
        using var context = database.CreateVerificationContext();
        var entityType = context.Model.FindEntityType(typeof(EmailOutboxMessage));
        if (entityType is null)
        {
            throw new Xunit.Sdk.XunitException("The Outbox entity is missing from the SQL model.");
        }

        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var columnNames = entityType.GetProperties()
            .Select(property => property.GetColumnName(table))
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        var expectedColumns = new HashSet<string>(StringComparer.Ordinal)
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
            "CredentialGenerationFingerprint",
            "RowVersion"
        };
        AssertTrue(columnNames.SetEquals(expectedColumns), "The Outbox schema contains unexpected persisted fields.");

        foreach (var property in entityType.GetProperties())
        {
            if (property.ClrType != typeof(string)
                || property.PropertyInfo?.GetValue(message) is not string storedValue)
            {
                continue;
            }

            AssertTrue(
                !sensitiveValues.Any(value =>
                    !string.IsNullOrEmpty(value)
                    && storedValue.Contains(value, StringComparison.Ordinal)),
                "Sensitive email data was persisted in the Outbox.");
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

    private static ServiceProvider CreateEphemeralDataProtectionServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        return services.BuildServiceProvider();
    }

    private static void AssertSafeEqual<T>(T expected, T? actual, string field)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Xunit.Sdk.XunitException($"SQL Server integration {field} did not match.");
        }
    }

    private static void AssertSafeDifferent<T>(T first, T second, string field)
    {
        var valuesAreEqual = first is byte[] firstBytes && second is byte[] secondBytes
            ? firstBytes.AsSpan().SequenceEqual(secondBytes)
            : EqualityComparer<T>.Default.Equals(first, second);
        if (valuesAreEqual)
        {
            throw new Xunit.Sdk.XunitException($"SQL Server integration {field} were unexpectedly equal.");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new Xunit.Sdk.XunitException(message);
        }
    }

    private static void AssertSafeTrue(bool condition, string message) =>
        AssertTrue(condition, message);

    private static void AssertEqualFingerprint(byte[] expected, byte[]? actual) =>
        AssertTrue(
            actual is not null && expected.AsSpan().SequenceEqual(actual),
            "The credential generation fingerprint changed unexpectedly.");

    private static string GetOnlyRecipient(ResendPayload payload)
    {
        if (payload.Recipients.Length != 1)
        {
            throw new Xunit.Sdk.XunitException("The captured Resend request has an invalid recipient count.");
        }

        return payload.Recipients[0];
    }

    private sealed record ClientAccount(Guid UserId, string Email);
    private sealed record ResetLink(string Link, string Email, string Token);
    private sealed record ResendPayload(string From, string[] Recipients, string Subject, string Html);

    private sealed class RecordingResendHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<HttpStatusCode> _responses;
        private readonly bool _pauseBeforeResponse;
        private readonly string _responseBody;
        private readonly TaskCompletionSource<bool> _responsePaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<string> _requestBodies = new();
        private int _requestCount;

        public RecordingResendHandler(
            HttpStatusCode responseStatus,
            string responseBody = SyntheticProviderError,
            bool pauseBeforeResponse = false)
            : this([responseStatus], responseBody, pauseBeforeResponse)
        {
        }

        public RecordingResendHandler(
            HttpStatusCode firstResponseStatus,
            HttpStatusCode secondResponseStatus,
            string responseBody = SyntheticProviderError)
            : this([firstResponseStatus, secondResponseStatus], responseBody, false)
        {
        }

        private RecordingResendHandler(
            IEnumerable<HttpStatusCode> responseStatuses,
            string responseBody,
            bool pauseBeforeResponse)
        {
            _responses = new ConcurrentQueue<HttpStatusCode>(responseStatuses);
            _responseBody = responseBody;
            _pauseBeforeResponse = pauseBeforeResponse;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string ResponseBody => _responseBody;
        public TaskCompletionSource<bool> ResponsePaused => _responsePaused;

        public void AllowResponse() => _allowResponse.TrySetResult(true);

        public ResendPayload ReadPayload(int index)
        {
            var requestBodies = _requestBodies.ToArray();
            if (index < 0 || index >= requestBodies.Length)
            {
                throw new Xunit.Sdk.XunitException("The fake Resend handler did not capture the expected request.");
            }

            try
            {
                using var document = JsonDocument.Parse(requestBodies[index]);
                var root = document.RootElement;
                var recipients = root.GetProperty("to");
                if (recipients.ValueKind != JsonValueKind.Array
                    || recipients.GetArrayLength() != 1)
                {
                    throw new Xunit.Sdk.XunitException("The captured Resend request has an invalid recipient list.");
                }

                return new ResendPayload(
                    root.GetProperty("from").GetString()!,
                    [recipients[0].GetString()!],
                    root.GetProperty("subject").GetString()!,
                    root.GetProperty("html").GetString()!);
            }
            catch (JsonException)
            {
                throw new Xunit.Sdk.XunitException("The fake Resend handler received an invalid request.");
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            _requestBodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));

            if (_pauseBeforeResponse)
            {
                _responsePaused.TrySetResult(true);
                await _allowResponse.Task.WaitAsync(cancellationToken);
            }

            if (!_responses.TryDequeue(out var statusCode))
            {
                throw new InvalidOperationException("The fake Resend handler has no response configured.");
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(_responseBody)
            };
        }
    }

    private sealed class PasswordResetProtectionGate
    {
        private int _hasPaused;
        private readonly TaskCompletionSource<bool> _protectionStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowProtection =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ProtectionStarted => _protectionStarted;

        public void PauseOnce()
        {
            if (Interlocked.CompareExchange(ref _hasPaused, 1, 0) != 0)
            {
                return;
            }

            _protectionStarted.TrySetResult(true);
            _allowProtection.Task.GetAwaiter().GetResult();
        }

        public void AllowProtection() => _allowProtection.TrySetResult(true);
    }

    private sealed class SynchronizedDataProtectionProvider(
        IDataProtectionProvider inner,
        PasswordResetProtectionGate gate) : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) =>
            new SynchronizedDataProtector(
                inner.CreateProtector(purpose),
                gate,
                string.Equals(
                    purpose,
                    XaquiamuniPasswordResetTokenProvider.ProtectionPurpose,
                    StringComparison.Ordinal));
    }

    private sealed class SynchronizedDataProtector(
        IDataProtector inner,
        PasswordResetProtectionGate gate,
        bool intercept) : IDataProtector
    {
        public byte[] Protect(byte[] plaintext)
        {
            if (intercept)
            {
                gate.PauseOnce();
            }

            return inner.Protect(plaintext);
        }

        public byte[] Unprotect(byte[] protectedData) => inner.Unprotect(protectedData);

        public IDataProtector CreateProtector(string purpose) =>
            new SynchronizedDataProtector(inner.CreateProtector(purpose), gate, intercept);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = nameof(PasswordResetResendSqlServerIntegrationTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
