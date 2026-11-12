using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class AuthPasswordRecoverySqlServerIntegrationTests
{
    private const string OriginalPassword = "Original Password 987!";
    private const string NewPassword = "  Replacement Password 987!  ";
    private const string GenericForgotPasswordMessage =
        "Se existir uma conta elegível para recuperação, você receberá instruções para redefinir sua senha.";
    private const string GenericResetPasswordMessage =
        "Não foi possível redefinir a senha. O link de recuperação pode ser inválido ou ter expirado. Solicite uma nova recuperação.";
    private const string RateLimitMessage = "Muitas solicitações. Tente novamente mais tarde.";
    private const string ResetPurpose = "ResetPassword";
    private static readonly IPAddress TestRemoteIpAddress = IPAddress.Parse("203.0.113.80");

    [SqlServerIntegrationFact]
    public async Task ForgotAndResetEndpoints_ProcessRealEmailAndInvalidatePreviousSession()
    {
        await using var environment = await CreateEnvironmentAsync();
        var account = await CreateRegisteredClientAsync(environment.Database);
        await ConfirmAccountAsync(environment.Database, account);

        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);
        var login = await LoginAsync(client, csrf, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.NotNull(login.AuthCookie);
        using (var authenticated = await GetMeAsync(client, login.AuthCookie!))
        {
            Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        }

        using var anonymousBefore = await GetMeAsync(client, authCookie: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousBefore.StatusCode);

        var resendHandler = new CapturingResendHandler();
        using var resendHttpClient = new HttpClient(resendHandler);
        var emailSender = new ResendEmailSender(
            resendHttpClient,
            Options.Create(new ResendEmailOptions
            {
                ApiKey = "test_key_without_external_service",
                FromEmail = "no-reply@example.test"
            }));

        using (var forgotResponse = await SendForgotPasswordAsync(client, csrf, account.Email))
        {
            await AssertForgotAcceptedAsync(forgotResponse);
            Assert.False(HasAuthCookie(forgotResponse));
        }

        Assert.Equal(0, resendHandler.RequestCount);
        var queuedMessage = Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));
        Assert.Equal(EmailOutboxMessageStatus.Pending, queuedMessage.Status);
        Assert.Equal(0, queuedMessage.Attempts);
        AssertSafeEqual($"password-reset:{account.Id:N}", queuedMessage.ActiveDeduplicationKey);
        Assert.True(await FingerprintMatchesAsync(
            environment.Database,
            account.Id,
            queuedMessage.CredentialGenerationFingerprint));
        await AssertOutboxDoesNotPersistRecipientOrTokenAsync(environment.Database);

        await using (var processorScope = environment.Database.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope())
        {
            var services = processorScope.ServiceProvider;
            var outboxStore = services.GetRequiredService<IEmailOutboxStore>();
            var claim = Assert.Single(await outboxStore.ClaimDueAsync(
                EmailOutboxMessageType.PasswordReset,
                batchSize: 1,
                leaseDuration: TimeSpan.FromMinutes(2)));
            var composer = new PasswordResetMessageComposer(
                Options.Create(new EmailConfirmationEmailOptions
                {
                    PublicFrontendBaseUrl = "https://frontend.example.test"
                }),
                environment.Factory.Services.GetRequiredService<IHostEnvironment>());
            var processor = new PasswordResetOutboxProcessor(
                outboxStore,
                services.GetRequiredService<IPasswordResetService>(),
                composer,
                emailSender,
                services.GetRequiredService<TimeProvider>());

            await processor.ProcessAsync(claim);
        }

        Assert.Equal(1, resendHandler.RequestCount);
        var resetLink = ReadResetLink(resendHandler.Payload, account.Email);
        var completedMessage = Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));
        Assert.Equal(EmailOutboxMessageStatus.Completed, completedMessage.Status);
        Assert.Equal(1, completedMessage.Attempts);
        Assert.Null(completedMessage.ActiveDeduplicationKey);
        var completedSnapshot = CaptureOutboxState(completedMessage);
        var confirmationSnapshots = (await ReadMessagesAsync(environment.Database, account.Id))
            .Where(message => message.MessageType == EmailOutboxMessageType.EmailConfirmation)
            .Select(CaptureOutboxState)
            .ToArray();
        Assert.NotEmpty(confirmationSnapshots);

        using (var resetResponse = await SendResetPasswordAsync(
            client,
            csrf,
            resetLink.Email,
            resetLink.Token,
            NewPassword))
        {
            Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);
            AssertNoStore(resetResponse);
            Assert.Empty(await resetResponse.Content.ReadAsByteArrayAsync());
            Assert.False(HasAuthCookie(resetResponse));
        }

        using (var anonymousAfter = await GetMeAsync(client, authCookie: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousAfter.StatusCode);
        }

        using (var staleSession = await GetMeAsync(client, login.AuthCookie!))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, staleSession.StatusCode);
            AssertNoStore(staleSession);
        }

        using (var reusedToken = await SendResetPasswordAsync(
            client,
            csrf,
            account.Email,
            resetLink.Token,
            "Another Password 987!"))
        {
            await AssertInvalidResetResponseAsync(
                reusedToken,
                account.Email,
                resetLink.Token,
                "Another Password 987!");
        }

        Assert.True(await CheckPasswordAsync(environment.Database, account.Id, NewPassword));
        Assert.False(await CheckPasswordAsync(environment.Database, account.Id, OriginalPassword));
        Assert.False(await VerifyResetTokenAsync(environment.Database, account.Id, resetLink.Token));
        AssertSameOutboxState(
            completedSnapshot,
            CaptureOutboxState(Assert.Single(await ReadResetMessagesAsync(
                environment.Database,
                account.Id))));
        var confirmationMessagesAfter = (await ReadMessagesAsync(environment.Database, account.Id))
            .Where(message => message.MessageType == EmailOutboxMessageType.EmailConfirmation)
            .Select(CaptureOutboxState)
            .ToArray();
        Assert.Equal(confirmationSnapshots.Length, confirmationMessagesAfter.Length);
        for (var index = 0; index < confirmationSnapshots.Length; index++)
        {
            AssertSameOutboxState(confirmationSnapshots[index], confirmationMessagesAfter[index]);
        }

        await using (var verification = environment.Database.CreateVerificationContext())
        {
            var profile = await verification.ClientePerfis.AsNoTracking()
                .SingleAsync(candidate => candidate.UserId == account.Id);
            AssertSafeEqual("Cliente Integração", profile.NomeCompleto);
            var address = await verification.ClienteEnderecos.AsNoTracking()
                .SingleAsync(candidate => candidate.ClienteUserId == account.Id);
            AssertSafeEqual("Vacaria", address.Cidade);
        }

        await using (var scope = environment.Database.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
            var user = await userManager.FindByIdAsync(account.Id.ToString());
            Assert.NotNull(user);
            Assert.Contains(XaquiamuniRoles.Cliente, await userManager.GetRolesAsync(user));
        }

        Assert.True((await ReadUserStateAsync(environment.Database, account.Id)).EmailConfirmed);
        var newLogin = await LoginAsync(
            client,
            await GetAntiforgeryAsync(client),
            account.Email,
            NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, newLogin.StatusCode);
        Assert.NotNull(newLogin.AuthCookie);
        using var newSession = await GetMeAsync(client, newLogin.AuthCookie!);
        Assert.Equal(HttpStatusCode.OK, newSession.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task ForgotPassword_ReturnsTheSameAcceptedResponseForEligibleMissingAndUnconfirmedAccounts()
    {
        await using var environment = await CreateEnvironmentAsync();
        var eligible = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        var unconfirmed = await CreateIdentityUserAsync(environment.Database, emailConfirmed: false);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);

        string? commonBody = null;
        foreach (var email in new[] { eligible.Email, CreateEmail(), unconfirmed.Email })
        {
            using var response = await SendForgotPasswordAsync(client, csrf, email);
            var body = await AssertForgotAcceptedAsync(response);
            if (commonBody is null)
            {
                commonBody = body;
            }
            else
            {
                AssertSafeEqual(commonBody, body);
            }
        }

        Assert.Single(await ReadResetMessagesAsync(environment.Database, eligible.Id));
        Assert.Empty(await ReadResetMessagesAsync(environment.Database, unconfirmed.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ForgotPassword_DeduplicatesPendingAndProcessingMessagesAndHonorsCooldown()
    {
        await using var environment = await CreateEnvironmentAsync();
        var account = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);

        using (var first = await SendForgotPasswordAsync(client, csrf, account.Email))
        {
            await AssertForgotAcceptedAsync(first);
        }

        var originalMessage = Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));
        await using (var scope = environment.Database.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>();
            var claim = Assert.Single(await store.ClaimDueAsync(
                EmailOutboxMessageType.PasswordReset,
                batchSize: 1,
                leaseDuration: TimeSpan.FromMinutes(2)));
            using (var whileProcessing = await SendForgotPasswordAsync(client, csrf, account.Email))
            {
                await AssertForgotAcceptedAsync(whileProcessing);
            }

            Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));

            await MarkCompletedAsync(
                environment.Database,
                originalMessage.Id,
                createdMinutesAgo: 2,
                completedMinutesAgo: 1);
            using (var cooldown = await SendForgotPasswordAsync(client, csrf, account.Email))
            {
                await AssertForgotAcceptedAsync(cooldown);
            }

            Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));
            await MarkCompletedAsync(
                environment.Database,
                originalMessage.Id,
                createdMinutesAgo: 6,
                completedMinutesAgo: 5);
            using (var outsideCooldown = await SendForgotPasswordAsync(client, csrf, account.Email))
            {
                await AssertForgotAcceptedAsync(outsideCooldown);
            }
        }

        var messages = await ReadResetMessagesAsync(environment.Database, account.Id);
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, message => message.Status == EmailOutboxMessageStatus.Completed);
        Assert.Contains(messages, message => message.Status == EmailOutboxMessageStatus.Pending);
    }

    [SqlServerIntegrationFact]
    public async Task ForgotPassword_HourlyLimitReturnsTheGenericResponseWithoutEnqueueing()
    {
        await using var environment = await CreateEnvironmentAsync();
        var account = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        await SeedCompletedResetHistoryAsync(environment.Database, account.Id, messageCount: 5);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);

        using var response = await SendForgotPasswordAsync(client, csrf, account.Email);

        await AssertForgotAcceptedAsync(response);
        var messages = await ReadResetMessagesAsync(environment.Database, account.Id);
        Assert.Equal(5, messages.Count);
        Assert.All(messages, message => Assert.Equal(EmailOutboxMessageStatus.Completed, message.Status));
    }

    [SqlServerIntegrationFact]
    public async Task ConfirmationEmailHistoryDoesNotConsumePasswordRecoveryAccountLimits()
    {
        await using var environment = await CreateEnvironmentAsync();
        var account = await CreateRegisteredClientAsync(environment.Database);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);

        for (var index = 0; index < 4; index++)
        {
            var confirmationMessage = await ReadLatestMessageAsync(
                environment.Database,
                account.Id,
                EmailOutboxMessageType.EmailConfirmation);
            await MarkCompletedAsync(
                environment.Database,
                confirmationMessage.Id,
                createdMinutesAgo: 5,
                completedMinutesAgo: 4);

            using var resend = await SendResendConfirmationAsync(client, csrf, account.Email);
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
            AssertNoStore(resend);
        }

        Assert.Equal(
            5,
            (await ReadMessagesAsync(environment.Database, account.Id))
                .Count(message => message.MessageType == EmailOutboxMessageType.EmailConfirmation));
        await ConfirmAccountAsync(environment.Database, account);

        using var forgot = await SendForgotPasswordAsync(client, csrf, account.Email);

        await AssertForgotAcceptedAsync(forgot);
        Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ResetPassword_MapsInvalidTokensAndAccountsGenericallyAndKeepsValidTokensForPasswordErrors()
    {
        await using var environment = await CreateEnvironmentAsync();
        var eligible = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        var unconfirmed = await CreateIdentityUserAsync(environment.Database, emailConfirmed: false);
        var other = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        var eligibleToken = await GenerateResetTokenAsync(environment.Factory, eligible.Id);
        var unconfirmedToken = await GenerateResetTokenAsync(environment.Factory, unconfirmed.Id);
        var otherToken = await GenerateResetTokenAsync(environment.Factory, other.Id);
        var confirmationToken = await GenerateEmailConfirmationTokenAsync(environment.Factory, eligible.Id);
        var tamperedToken = TamperResetToken(eligibleToken);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);

        using (var weakPassword = await SendResetPasswordAsync(
            client,
            csrf,
            eligible.Email,
            eligibleToken,
            "weak"))
        {
            var body = await AssertInvalidPasswordResponseAsync(
                weakPassword,
                eligible.Email,
                eligibleToken,
                "weak");
            AssertTextNotPresent(body, "PasswordHash", StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(await VerifyResetTokenAsync(environment.Database, eligible.Id, eligibleToken));
        var genericBodies = new List<string>();
        foreach (var (email, token, password) in new[]
        {
            (eligible.Email, "invalid-reset-token", NewPassword),
            (eligible.Email, tamperedToken, NewPassword),
            (eligible.Email, confirmationToken, NewPassword),
            (eligible.Email, "invalid-reset-token", "weak"),
            (CreateEmail(), "unknown-account-token", NewPassword),
            (unconfirmed.Email, unconfirmedToken, NewPassword),
            (eligible.Email, otherToken, NewPassword)
        })
        {
            using var response = await SendResetPasswordAsync(client, csrf, email, token, password);
            genericBodies.Add(await AssertInvalidResetResponseAsync(
                response,
                email,
                token,
                password));
        }

        Assert.All(genericBodies.Skip(1), body => AssertSafeEqual(genericBodies[0], body));
        Assert.True(await CheckPasswordAsync(environment.Database, eligible.Id, OriginalPassword));
        Assert.True(await VerifyResetTokenAsync(environment.Database, eligible.Id, eligibleToken));
        Assert.True(await CheckPasswordAsync(environment.Database, other.Id, OriginalPassword));
        Assert.True(await CheckPasswordAsync(environment.Database, unconfirmed.Id, OriginalPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ResetPassword_ExpiredTokenUsesTheSameGenericResponse()
    {
        await using var environment = await CreateEnvironmentAsync(
            tokenLifespan: TimeSpan.FromTicks(-1));
        var account = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        var token = await GenerateResetTokenAsync(environment.Factory, account.Id);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);

        using var response = await SendResetPasswordAsync(
            client,
            csrf,
            account.Email,
            token,
            NewPassword);

        await AssertInvalidResetResponseAsync(response, account.Email, token, NewPassword);
        Assert.True(await CheckPasswordAsync(environment.Database, account.Id, OriginalPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ResetPassword_CancelsPendingAndProcessingMessagesAndPreservesHistory()
    {
        await using var environment = await CreateEnvironmentAsync();
        var pendingAccount = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        var processingAccount = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        var deadLetterBefore = await SeedDeadLetterHistoryAsync(
            environment.Database,
            pendingAccount.Id);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);

        using (var forgot = await SendForgotPasswordAsync(client, csrf, processingAccount.Email))
        {
            await AssertForgotAcceptedAsync(forgot);
        }

        var processingBefore = Assert.Single(await ReadResetMessagesAsync(
            environment.Database,
            processingAccount.Id));

        EmailOutboxClaim processingClaim;
        await using (var scope = environment.Database.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope())
        {
            processingClaim = Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IEmailOutboxStore>()
                .ClaimDueAsync(
                    EmailOutboxMessageType.PasswordReset,
                    batchSize: 10,
                    leaseDuration: TimeSpan.FromMinutes(2)));
            AssertSafeEqual(processingBefore.Id, processingClaim.Id);
        }

        using (var forgot = await SendForgotPasswordAsync(client, csrf, pendingAccount.Email))
        {
            await AssertForgotAcceptedAsync(forgot);
        }

        var pendingBefore = Assert.Single(
            await ReadResetMessagesAsync(environment.Database, pendingAccount.Id),
            message => message.Status == EmailOutboxMessageStatus.Pending);
        var pendingToken = await GenerateResetTokenAsync(environment.Factory, pendingAccount.Id);
        var processingToken = await GenerateResetTokenAsync(environment.Factory, processingAccount.Id);

        using (var pendingReset = await SendResetPasswordAsync(
            client,
            csrf,
            pendingAccount.Email,
            pendingToken,
            NewPassword))
        {
            Assert.Equal(HttpStatusCode.NoContent, pendingReset.StatusCode);
            AssertNoStore(pendingReset);
        }

        using (var processingReset = await SendResetPasswordAsync(
            client,
            csrf,
            processingAccount.Email,
            processingToken,
            NewPassword))
        {
            Assert.Equal(HttpStatusCode.NoContent, processingReset.StatusCode);
            AssertNoStore(processingReset);
        }

        var pendingAfter = Assert.Single(
            await ReadResetMessagesAsync(environment.Database, pendingAccount.Id),
            message => message.Id == pendingBefore.Id);
        var processingAfter = Assert.Single(await ReadResetMessagesAsync(
            environment.Database,
            processingAccount.Id));
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, pendingAfter.Status);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, processingAfter.Status);
        AssertSameHistoricalMessage(pendingBefore, pendingAfter, wasClaimed: false);
        AssertSameHistoricalMessage(processingBefore, processingAfter, wasClaimed: true);
        var deadLetterAfter = await ReadOutboxMessageAsync(
            environment.Database,
            deadLetterBefore.Id);
        AssertSameOutboxState(
            CaptureOutboxState(deadLetterBefore),
            CaptureOutboxState(deadLetterAfter));

        await using (var scope = environment.Database.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>();
            Assert.False(await store.CompleteAsync(processingClaim));
        }

        await AgeResetHistoryAsync(environment.Database, pendingAccount.Id, minutesAgo: 6);
        using (var nextRequest = await SendForgotPasswordAsync(client, csrf, pendingAccount.Email))
        {
            await AssertForgotAcceptedAsync(nextRequest);
        }

        var pendingHistory = await ReadResetMessagesAsync(environment.Database, pendingAccount.Id);
        Assert.Equal(3, pendingHistory.Count);
        Assert.Contains(pendingHistory, message => message.Status == EmailOutboxMessageStatus.DeadLetter);
        var nextMessage = Assert.Single(
            pendingHistory,
            message => message.Status == EmailOutboxMessageStatus.Pending);
        Assert.True(await FingerprintMatchesAsync(
            environment.Database,
            pendingAccount.Id,
            nextMessage.CredentialGenerationFingerprint));
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentForgotRequestsDeduplicateAndConcurrentResetsAllowOnlyOneWinner()
    {
        var requestBarrier = new SynchronizedUserLookupInterceptor();
        await using var environment = await CreateEnvironmentAsync(
            dbContextInterceptor: requestBarrier);
        var account = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        using var firstClient = environment.CreateClient();
        using var secondClient = environment.CreateClient();
        var firstCsrf = await GetAntiforgeryAsync(firstClient);
        var secondCsrf = await GetAntiforgeryAsync(secondClient);

        var forgotResponses = await RunSynchronizedRequestsAsync(
            requestBarrier,
            () => SendForgotPasswordAsync(firstClient, firstCsrf, account.Email),
            () => SendForgotPasswordAsync(secondClient, secondCsrf, account.Email));
        foreach (var response in forgotResponses)
        {
            using (response)
            {
                await AssertForgotAcceptedAsync(response);
            }
        }

        Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));
        var token = await GenerateResetTokenAsync(environment.Factory, account.Id);

        var resetResponses = await RunSynchronizedRequestsAsync(
            requestBarrier,
            () => SendResetPasswordAsync(
                firstClient,
                firstCsrf,
                account.Email,
                token,
                "Winner Password 987!"),
            () => SendResetPasswordAsync(
                secondClient,
                secondCsrf,
                account.Email,
                token,
                "Other Password 987!"));
        try
        {
            Assert.Equal(
                1,
                resetResponses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
            var rejected = Assert.Single(
                resetResponses,
                response => response.StatusCode == HttpStatusCode.BadRequest);
            await AssertInvalidResetResponseAsync(rejected, account.Email, token, "Other Password 987!");
            Assert.All(resetResponses, AssertNoStore);
            Assert.All(resetResponses, response => Assert.False(HasAuthCookie(response)));
        }
        finally
        {
            foreach (var response in resetResponses)
            {
                response.Dispose();
            }
        }

        var winnerPasswordIsStored =
            await CheckPasswordAsync(environment.Database, account.Id, "Winner Password 987!");
        var otherPasswordIsStored =
            await CheckPasswordAsync(environment.Database, account.Id, "Other Password 987!");
        Assert.NotEqual(winnerPasswordIsStored, otherPasswordIsStored);
        Assert.False(await CheckPasswordAsync(environment.Database, account.Id, OriginalPassword));
        Assert.Equal(
            EmailOutboxMessageStatus.Cancelled,
            Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id)).Status);
    }

    [SqlServerIntegrationFact]
    public async Task AntiforgeryAndRateLimitsRejectRecoveryRequestsWithoutSideEffects()
    {
        await using var environment = await CreateEnvironmentAsync();
        var firstAccount = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        var secondAccount = await CreateIdentityUserAsync(environment.Database, emailConfirmed: true);
        using var client = environment.CreateClient();
        using var antiforgeryFailureClient = environment.CreateClient(IPAddress.Parse("203.0.113.81"));
        var csrf = await GetAntiforgeryAsync(client);

        using (var noAntiforgery = await SendForgotPasswordWithoutAntiforgeryAsync(
            antiforgeryFailureClient,
            firstAccount.Email))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noAntiforgery.StatusCode);
            AssertNoStore(noAntiforgery);
        }

        var invalidCsrf = new AntiforgeryState("invalid-request-token", csrf.CookiePair);
        using (var invalidAntiforgery = await SendForgotPasswordAsync(
            antiforgeryFailureClient,
            invalidCsrf,
            firstAccount.Email))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidAntiforgery.StatusCode);
            AssertNoStore(invalidAntiforgery);
        }

        var secondToken = await GenerateResetTokenAsync(environment.Factory, secondAccount.Id);
        using (var invalidAntiforgery = await SendResetPasswordWithoutAntiforgeryAsync(
            antiforgeryFailureClient,
            secondAccount.Email,
            secondToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidAntiforgery.StatusCode);
            AssertNoStore(invalidAntiforgery);
        }

        using (var invalidAntiforgery = await SendResetPasswordAsync(
            antiforgeryFailureClient,
            invalidCsrf,
            secondAccount.Email,
            secondToken,
            NewPassword))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidAntiforgery.StatusCode);
            AssertNoStore(invalidAntiforgery);
        }

        Assert.Empty(await ReadResetMessagesAsync(environment.Database, firstAccount.Id));
        Assert.True(await VerifyResetTokenAsync(environment.Database, secondAccount.Id, secondToken));

        for (var index = 0; index < 10; index++)
        {
            using var response = await SendForgotPasswordAsync(client, csrf, firstAccount.Email);
            await AssertForgotAcceptedAsync(response);
        }

        using (var overLimitForgot = await SendForgotPasswordAsync(client, csrf, secondAccount.Email))
        {
            await AssertRateLimited(overLimitForgot);
        }

        Assert.Single(await ReadResetMessagesAsync(environment.Database, firstAccount.Id));
        Assert.Empty(await ReadResetMessagesAsync(environment.Database, secondAccount.Id));

        for (var index = 0; index < 10; index++)
        {
            using var response = await SendResetPasswordAsync(
                client,
                csrf,
                firstAccount.Email,
                "invalid-reset-token",
                NewPassword);
            await AssertInvalidResetResponseAsync(
                response,
                firstAccount.Email,
                "invalid-reset-token",
                NewPassword);
        }

        var secondAccountState = await ReadUserStateAsync(environment.Database, secondAccount.Id);
        using (var overLimitReset = await SendResetPasswordAsync(
            client,
            csrf,
            secondAccount.Email,
            secondToken,
            NewPassword))
        {
            await AssertRateLimited(overLimitReset);
        }

        AssertSameCredentialState(
            secondAccountState,
            await ReadUserStateAsync(environment.Database, secondAccount.Id));
        Assert.True(await VerifyResetTokenAsync(environment.Database, secondAccount.Id, secondToken));
    }

    [SqlServerIntegrationFact]
    public async Task ResetPassword_CommitFailureRollsBackCredentialsAndOutboxWhileKeepingSessionValid()
    {
        var interceptor = new FailCommitAfterResetUpdatesInterceptor();
        await using var environment = await CreateEnvironmentAsync(dbContextInterceptor: interceptor);
        var account = await CreateRegisteredClientAsync(environment.Database);
        await ConfirmAccountAsync(environment.Database, account);
        using var client = environment.CreateClient();
        var csrf = await GetAntiforgeryAsync(client);
        var login = await LoginAsync(client, csrf, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.NotNull(login.AuthCookie);

        using (var forgot = await SendForgotPasswordAsync(client, csrf, account.Email))
        {
            await AssertForgotAcceptedAsync(forgot);
        }

        var message = Assert.Single(await ReadResetMessagesAsync(environment.Database, account.Id));
        var token = await GenerateResetTokenAsync(environment.Factory, account.Id);
        var userBefore = await ReadUserStateAsync(environment.Database, account.Id);
        var messageBefore = CaptureOutboxState(message);
        interceptor.Arm(account.Id, message.Id, userBefore);

        using var request = CreateResetRequest(
            csrf,
            account.Email,
            token,
            NewPassword);
        await Assert.ThrowsAsync<InjectedResetCommitFailureException>(
            async () => await client.SendAsync(request));

        Assert.True(interceptor.IdentityAndOutboxUpdatesVisibleBeforeCommit);
        AssertSameCredentialState(userBefore, await ReadUserStateAsync(environment.Database, account.Id));
        AssertSameOutboxState(
            messageBefore,
            CaptureOutboxState(Assert.Single(await ReadResetMessagesAsync(
                environment.Database,
                account.Id))));
        Assert.True(await VerifyResetTokenAsync(environment.Database, account.Id, token));
        Assert.True(await CheckPasswordAsync(environment.Database, account.Id, OriginalPassword));
        Assert.False(await CheckPasswordAsync(environment.Database, account.Id, NewPassword));

        using var existingSession = await GetMeAsync(client, login.AuthCookie);
        Assert.Equal(HttpStatusCode.OK, existingSession.StatusCode);
        AssertNoStore(existingSession);
    }

    private static async Task<TestEnvironment> CreateEnvironmentAsync(
        IInterceptor? dbContextInterceptor = null,
        TimeSpan? tokenLifespan = null)
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var database = await SqlServerTestDatabase.CreateAsync(
            dataProtectionProvider: dataProtectionProvider,
            dbContextInterceptor: dbContextInterceptor);
        try
        {
            var factory = CreateFactory(
                database,
                dataProtectionProvider,
                dbContextInterceptor,
                tokenLifespan);
            return new TestEnvironment(database, factory);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        SqlServerTestDatabase database,
        IDataProtectionProvider dataProtectionProvider,
        IInterceptor? dbContextInterceptor,
        TimeSpan? tokenLifespan)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");
        var encryptionKey = RandomNumberGenerator.GetBytes(32);
        var fingerprintKey = RandomNumberGenerator.GetBytes(32);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString,
                    ["CpfProtection:CurrentEncryptionKeyVersion"] = "enc-http-integration",
                    ["CpfProtection:EncryptionKeys:enc-http-integration"] =
                        Convert.ToBase64String(encryptionKey),
                    ["CpfProtection:CurrentFingerprintKeyVersion"] = "fp-http-integration",
                    ["CpfProtection:FingerprintKeys:fp-http-integration"] =
                        Convert.ToBase64String(fingerprintKey),
                    ["EmailConfirmation:PublicFrontendBaseUrl"] = "https://frontend.example.test",
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<XaquiamuniDbContext>(options =>
                {
                    options.UseSqlServer(connectionString);
                    if (dbContextInterceptor is not null)
                    {
                        options.AddInterceptors(dbContextInterceptor);
                    }
                });
                services.AddDataProtection();
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton(dataProtectionProvider);
                if (tokenLifespan is TimeSpan lifespan)
                {
                    services.Configure<XaquiamuniPasswordResetTokenProviderOptions>(
                        options => options.TokenLifespan = lifespan);
                }
            });
        });

        try
        {
            using var scope = factory.Services.CreateScope();
            var configuredConnectionString = scope.ServiceProvider
                .GetRequiredService<XaquiamuniDbContext>()
                .Database.GetConnectionString();
            var expectedConnection = new SqlConnectionStringBuilder(connectionString);
            var configuredConnection = new SqlConnectionStringBuilder(configuredConnectionString);
            if (!string.Equals(
                    configuredConnection.DataSource,
                    @".\SQLEXPRESS",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    configuredConnection.InitialCatalog,
                    expectedConnection.InitialCatalog,
                    StringComparison.Ordinal)
                || !configuredConnection.InitialCatalog.StartsWith(
                    "Xaquiamuni_IntegrationTest_",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The HTTP test host is not configured for its isolated local SQL test database.");
            }

            return factory;
        }
        catch
        {
            factory.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(fingerprintKey);
        }
    }

    private static async Task<IntegrationAccount> CreateRegisteredClientAsync(
        SqlServerTestDatabase database)
    {
        var email = CreateEmail();
        var result = await database.Services.GetRequiredService<ICadastrarCliente>()
            .ExecuteAsync(new CadastroClienteInput
            {
                NomeCompleto = "Cliente Integração",
                Cpf = "529.982.247-25",
                Email = email,
                Telefone = "+55 (11) 91234-5678",
                Senha = OriginalPassword,
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
        Assert.True(result.Succeeded, "The registered integration account could not be created.");
        return new IntegrationAccount(
            Assert.IsType<Guid>(result.UserId),
            email);
    }

    private static async Task<IntegrationAccount> CreateIdentityUserAsync(
        SqlServerTestDatabase database,
        bool emailConfirmed)
    {
        await using var scope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var email = CreateEmail();
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var createResult = await userManager.CreateAsync(user, OriginalPassword);
        Assert.True(createResult.Succeeded, "The Identity integration account could not be created.");
        return new IntegrationAccount(user.Id, email);
    }

    private static async Task ConfirmAccountAsync(
        SqlServerTestDatabase database,
        IntegrationAccount account)
    {
        var service = database.Services.GetRequiredService<IEmailConfirmationService>();
        var token = await service.GenerateEmailConfirmationTokenForUserAsync(account.Id);
        Assert.Equal(EmailConfirmationTokenStatus.Generated, token.Status);
        Assert.NotNull(token.Token);
        var result = await service.ConfirmEmailAsync(account.Email, token.Token!);
        Assert.Equal(EmailConfirmationStatus.Confirmed, result.Status);
    }

    private static async Task<string> GenerateResetTokenAsync(
        WebApplicationFactory<Program> factory,
        Guid userId)
    {
        await using var scope = factory.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token;
    }

    private static async Task<string> GenerateEmailConfirmationTokenAsync(
        WebApplicationFactory<Program> factory,
        Guid userId)
    {
        await using var scope = factory.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token;
    }

    private static string TamperResetToken(string token)
    {
        Assert.False(string.IsNullOrWhiteSpace(token));
        var replacement = token[0] == 'A' ? 'B' : 'A';
        return replacement + token[1..];
    }

    private static async Task<T[]> RunSynchronizedRequestsAsync<T>(
        SynchronizedUserLookupInterceptor interceptor,
        Func<Task<T>> first,
        Func<Task<T>> second)
    {
        interceptor.Arm(expectedParticipants: 2);
        var requests = Task.WhenAll(first(), second());
        try
        {
            await interceptor.WaitForParticipantsAsync(TimeSpan.FromSeconds(30));
        }
        catch
        {
            interceptor.Release();
            await requests.WaitAsync(TimeSpan.FromSeconds(30));
            throw;
        }

        interceptor.Release();
        return await requests.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<bool> VerifyResetTokenAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string token)
    {
        await using var scope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await userManager.VerifyUserTokenAsync(
            user,
            XaquiamuniPasswordResetTokenProvider.ProviderName,
            ResetPurpose,
            token);
    }

    private static async Task<bool> CheckPasswordAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string password)
    {
        await using var scope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await userManager.CheckPasswordAsync(user, password);
    }

    private static async Task<bool> FingerprintMatchesAsync(
        SqlServerTestDatabase database,
        Guid userId,
        byte[]? fingerprint)
    {
        if (fingerprint is null)
        {
            return false;
        }

        var userState = await ReadUserStateAsync(database, userId);
        await using var scope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<ICredentialGenerationFingerprint>()
            .Matches(userState.SecurityStamp, fingerprint);
    }

    private static async Task<UserCredentialState> ReadUserStateAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        var user = await context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
        return new UserCredentialState(
            user.PasswordHash!,
            user.SecurityStamp!,
            user.ConcurrencyStamp!,
            user.EmailConfirmed);
    }

    private static async Task<List<EmailOutboxMessage>> ReadResetMessagesAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages.AsNoTracking()
            .Where(message =>
                message.UserId == userId
                && message.MessageType == EmailOutboxMessageType.PasswordReset)
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
    }

    private static async Task<List<EmailOutboxMessage>> ReadMessagesAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages.AsNoTracking()
            .Where(message => message.UserId == userId)
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
    }

    private static async Task<EmailOutboxMessage> ReadLatestMessageAsync(
        SqlServerTestDatabase database,
        Guid userId,
        EmailOutboxMessageType messageType)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages.AsNoTracking()
            .Where(message => message.UserId == userId && message.MessageType == messageType)
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .FirstAsync();
    }

    private static async Task<EmailOutboxMessage> ReadOutboxMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Id == messageId);
    }

    private static async Task AssertOutboxDoesNotPersistRecipientOrTokenAsync(
        SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        var outboxType = context.Model.FindEntityType(typeof(EmailOutboxMessage));
        Assert.NotNull(outboxType);
        var properties = outboxType.GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("Email", properties, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", properties, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Link", properties, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task MarkCompletedAsync(
        SqlServerTestDatabase database,
        Guid messageId,
        int createdMinutesAgo,
        int completedMinutesAgo)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[EmailOutbox]
            SET [Status] = {(short)EmailOutboxMessageStatus.Completed},
                [Attempts] = CASE WHEN [Attempts] = 0 THEN 1 ELSE [Attempts] END,
                [CreatedAtUtc] = DATEADD(
                    minute,
                    {-createdMinutesAgo},
                    TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
                [NextAttemptAtUtc] = DATEADD(
                    minute,
                    {-createdMinutesAgo},
                    TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
                [CompletedAtUtc] = DATEADD(
                    minute,
                    {-completedMinutesAgo},
                    TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
                [ActiveDeduplicationKey] = NULL,
                [LeaseExpiresAtUtc] = NULL
            WHERE [Id] = {messageId};
            """);
    }

    private static async Task AgeResetHistoryAsync(
        SqlServerTestDatabase database,
        Guid userId,
        int minutesAgo)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[EmailOutbox]
            SET [CreatedAtUtc] = DATEADD(
                    minute,
                    {-minutesAgo},
                    TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
                [NextAttemptAtUtc] = DATEADD(
                    minute,
                    {-minutesAgo},
                    TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'))
            WHERE [UserId] = {userId}
                AND [MessageType] = {(short)EmailOutboxMessageType.PasswordReset};
            """);
    }

    private static async Task SeedCompletedResetHistoryAsync(
        SqlServerTestDatabase database,
        Guid userId,
        int messageCount)
    {
        var now = DateTimeOffset.UtcNow;
        var fingerprint = RandomNumberGenerator.GetBytes(32);
        try
        {
            await using var context = database.CreateVerificationContext();
            for (var index = 0; index < messageCount; index++)
            {
                var createdAtUtc = now.AddMinutes(-10 - index);
                context.EmailOutboxMessages.Add(new EmailOutboxMessage(
                    Guid.NewGuid(),
                    userId,
                    EmailOutboxMessageType.PasswordReset,
                    EmailOutboxMessageStatus.Completed,
                    attempts: 1,
                    createdAtUtc,
                    createdAtUtc,
                    createdAtUtc.AddMinutes(1),
                    credentialGenerationFingerprint: fingerprint));
            }

            await context.SaveChangesAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fingerprint);
        }
    }

    private static async Task<EmailOutboxMessage> SeedDeadLetterHistoryAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var fingerprint = RandomNumberGenerator.GetBytes(32);
        try
        {
            var message = new EmailOutboxMessage(
                Guid.NewGuid(),
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.DeadLetter,
                attempts: 5,
                now.AddMinutes(-10),
                now.AddMinutes(-10),
                credentialGenerationFingerprint: fingerprint);
            await using var context = database.CreateVerificationContext();
            context.EmailOutboxMessages.Add(message);
            await context.SaveChangesAsync();
            return await context.EmailOutboxMessages.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == message.Id);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fingerprint);
        }
    }

    private static OutboxState CaptureOutboxState(EmailOutboxMessage message) =>
        new(
            message.Id,
            message.UserId,
            message.MessageType,
            message.Status,
            message.Attempts,
            message.CreatedAtUtc,
            message.NextAttemptAtUtc,
            message.CompletedAtUtc,
            message.ActiveDeduplicationKey,
            message.LeaseExpiresAtUtc,
            message.CredentialGenerationFingerprint,
            message.RowVersion.ToArray());

    private static void AssertSameHistoricalMessage(
        EmailOutboxMessage before,
        EmailOutboxMessage after,
        bool wasClaimed)
    {
        AssertSafeEqual(before.Id, after.Id);
        AssertSafeEqual(before.UserId, after.UserId);
        Assert.Equal(before.MessageType, after.MessageType);
        Assert.Equal(before.Attempts + (wasClaimed ? 1 : 0), after.Attempts);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
        Assert.Equal(before.NextAttemptAtUtc, after.NextAttemptAtUtc);
        Assert.Null(after.CompletedAtUtc);
        Assert.Null(after.ActiveDeduplicationKey);
        Assert.Null(after.LeaseExpiresAtUtc);
        Assert.True(before.CredentialGenerationFingerprint!.SequenceEqual(
            after.CredentialGenerationFingerprint!));
    }

    private static void AssertSameOutboxState(OutboxState expected, OutboxState actual)
    {
        AssertSafeEqual(expected.Id, actual.Id);
        AssertSafeEqual(expected.UserId, actual.UserId);
        Assert.Equal(expected.MessageType, actual.MessageType);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Attempts, actual.Attempts);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.NextAttemptAtUtc, actual.NextAttemptAtUtc);
        Assert.Equal(expected.CompletedAtUtc, actual.CompletedAtUtc);
        AssertSafeEqual(expected.ActiveDeduplicationKey, actual.ActiveDeduplicationKey);
        Assert.Equal(expected.LeaseExpiresAtUtc, actual.LeaseExpiresAtUtc);
        Assert.True(expected.CredentialGenerationFingerprint!.SequenceEqual(
            actual.CredentialGenerationFingerprint!));
        Assert.True(expected.RowVersion.SequenceEqual(actual.RowVersion));
    }

    private static void AssertSameCredentialState(
        UserCredentialState expected,
        UserCredentialState actual)
    {
        AssertSafeEqual(expected.PasswordHash, actual.PasswordHash);
        AssertSafeEqual(expected.SecurityStamp, actual.SecurityStamp);
        AssertSafeEqual(expected.ConcurrencyStamp, actual.ConcurrencyStamp);
        Assert.Equal(expected.EmailConfirmed, actual.EmailConfirmed);
    }

    private static async Task<AntiforgeryState> GetAntiforgeryAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestToken = document.RootElement.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestToken));
        var cookie = FindCookiePair(response, AntiforgeryConfiguration.CookieName);
        return new AntiforgeryState(requestToken!, cookie);
    }

    private static async Task<LoginResult> LoginAsync(
        HttpClient client,
        AntiforgeryState csrf,
        string email,
        string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(new LoginClientRequest
            {
                Email = email,
                Senha = password
            })
        };
        AddAntiforgery(request, csrf);
        using var response = await client.SendAsync(request);
        return new LoginResult(
            response.StatusCode,
            TryFindCookiePair(response, AuthenticationConfiguration.CookieName));
    }

    private static Task<HttpResponseMessage> SendForgotPasswordAsync(
        HttpClient client,
        AntiforgeryState csrf,
        string email) =>
        SendJsonAsync(
            client,
            AuthEndpoints.ForgotPasswordEndpoint,
            new { email },
            csrf);

    private static Task<HttpResponseMessage> SendResetPasswordAsync(
        HttpClient client,
        AntiforgeryState csrf,
        string email,
        string token,
        string newPassword) =>
        SendJsonAsync(
            client,
            AuthEndpoints.ResetPasswordEndpoint,
            new { email, token, newPassword },
            csrf);

    private static Task<HttpResponseMessage> SendResendConfirmationAsync(
        HttpClient client,
        AntiforgeryState csrf,
        string email) =>
        SendJsonAsync(
            client,
            AuthEndpoints.ResendConfirmationEmailEndpoint,
            new { email },
            csrf);

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        string endpoint,
        T payload,
        AntiforgeryState csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        AddAntiforgery(request, csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendForgotPasswordWithoutAntiforgeryAsync(
        HttpClient client,
        string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.ForgotPasswordEndpoint)
        {
            Content = JsonContent.Create(new { email })
        };
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendResetPasswordWithoutAntiforgeryAsync(
        HttpClient client,
        string email,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.ResetPasswordEndpoint)
        {
            Content = JsonContent.Create(new { email, token, newPassword = NewPassword })
        };
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateResetRequest(
        AntiforgeryState csrf,
        string email,
        string token,
        string newPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.ResetPasswordEndpoint)
        {
            Content = JsonContent.Create(new { email, token, newPassword })
        };
        AddAntiforgery(request, csrf);
        return request;
    }

    private static void AddAntiforgery(HttpRequestMessage request, AntiforgeryState csrf)
    {
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        request.Headers.Add("Cookie", csrf.CookiePair);
    }

    private static async Task<string> AssertForgotAcceptedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        AssertNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        AssertSafeEqual(
            GenericForgotPasswordMessage,
            document.RootElement.GetProperty("message").GetString());
        return body;
    }

    private static async Task<string> AssertInvalidResetResponseAsync(
        HttpResponseMessage response,
        string email,
        string token,
        string password)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        AssertSafeEqual(
            GenericResetPasswordMessage,
            document.RootElement.GetProperty("detail").GetString());
        AssertTextNotPresent(body, email, StringComparison.Ordinal);
        AssertTextNotPresent(body, token, StringComparison.Ordinal);
        AssertTextNotPresent(body, password, StringComparison.Ordinal);
        return body;
    }

    private static async Task<string> AssertInvalidPasswordResponseAsync(
        HttpResponseMessage response,
        string email,
        string token,
        string password)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NewPassword", body, StringComparison.Ordinal);
        AssertTextNotPresent(body, email, StringComparison.Ordinal);
        AssertTextNotPresent(body, token, StringComparison.Ordinal);
        AssertTextNotPresent(body, password, StringComparison.Ordinal);
        return body;
    }

    private static async Task<HttpResponseMessage> GetMeAsync(
        HttpClient client,
        string? authCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AuthEndpoints.MeEndpoint);
        if (authCookie is not null)
        {
            request.Headers.Add("Cookie", authCookie);
        }

        return await client.SendAsync(request);
    }

    private static ResetLink ReadResetLink(string? payload, string expectedEmail)
    {
        Assert.False(string.IsNullOrWhiteSpace(payload));
        using var document = JsonDocument.Parse(payload!);
        var recipient = document.RootElement.GetProperty("to")[0].GetString();
        AssertSafeEqual(expectedEmail, recipient);
        var html = WebUtility.HtmlDecode(document.RootElement.GetProperty("html").GetString())
            ?? throw new Xunit.Sdk.XunitException("The generated reset message has no HTML body.");
        var hrefIndex = html.IndexOf("href=\"", StringComparison.Ordinal);
        Assert.True(hrefIndex >= 0);
        var start = hrefIndex + "href=\"".Length;
        var end = html.IndexOf('"', start);
        Assert.True(end > start);
        Assert.True(Uri.TryCreate(html[start..end], UriKind.Absolute, out var link));
        var resetUri = link
            ?? throw new Xunit.Sdk.XunitException("The generated reset link is invalid.");
        Assert.EndsWith("/reset-password", resetUri.AbsolutePath, StringComparison.Ordinal);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in resetUri.Fragment.TrimStart('#').Split('&'))
        {
            var separator = entry.IndexOf('=');
            Assert.True(separator > 0);
            values[entry[..separator]] = Uri.UnescapeDataString(entry[(separator + 1)..]);
        }

        Assert.True(values.TryGetValue("email", out var email));
        Assert.True(values.TryGetValue("token", out var token));
        return new ResetLink(email!, token!);
    }

    private static async Task AssertRateLimited(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertNoStore(response);
        Assert.True(response.Headers.RetryAfter is not null);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        AssertSafeEqual(
            RateLimitMessage,
            document.RootElement.GetProperty("message").GetString());
    }

    private static string FindCookiePair(HttpResponseMessage response, string cookieName)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(value => value.StartsWith(cookieName + "=", StringComparison.Ordinal));
        Assert.NotNull(cookie);
        return cookie!.Split(';', 2)[0];
    }

    private static string? TryFindCookiePair(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        var cookie = values.FirstOrDefault(value =>
            value.StartsWith(cookieName + "=", StringComparison.Ordinal));
        return cookie?.Split(';', 2)[0];
    }

    private static bool HasAuthCookie(HttpResponseMessage response) =>
        TryFindCookiePair(response, AuthenticationConfiguration.CookieName) is not null;

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);

    private static void AssertSafeEqual(string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new Xunit.Sdk.XunitException("A sensitive integration-test value did not match.");
        }
    }

    private static void AssertSafeEqual(Guid expected, Guid actual)
    {
        if (expected != actual)
        {
            throw new Xunit.Sdk.XunitException("An integration-test identifier did not match.");
        }
    }

    private static void AssertTextNotPresent(
        string text,
        string value,
        StringComparison comparison)
    {
        if (text.Contains(value, comparison))
        {
            throw new Xunit.Sdk.XunitException(
                "The HTTP response contained a value that must remain private.");
        }
    }

    private static string CreateEmail() =>
        $"password-recovery-http-{Guid.NewGuid():N}@example.test";

    private sealed class TestEnvironment(
        SqlServerTestDatabase database,
        WebApplicationFactory<Program> factory) : IAsyncDisposable
    {
        public SqlServerTestDatabase Database { get; } = database;
        public WebApplicationFactory<Program> Factory { get; } = factory;

        public HttpClient CreateClient() =>
            CreateClient(TestRemoteIpAddress);

        public HttpClient CreateClient(IPAddress remoteIpAddress) =>
            new(Factory.Server.CreateHandler(context =>
            {
                context.Connection.RemoteIpAddress = remoteIpAddress;
            }))
            {
                BaseAddress = new Uri("https://localhost")
            };

        public async ValueTask DisposeAsync()
        {
            Factory.Dispose();
            await Database.DisposeAsync();
        }
    }

    private sealed class CapturingResendHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);
        public string? Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post
                || request.RequestUri != new Uri("https://api.resend.com/emails"))
            {
                throw new InvalidOperationException("The simulated email sender received an unexpected request.");
            }

            Interlocked.Increment(ref _requestCount);
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class SynchronizedUserLookupInterceptor : DbCommandInterceptor
    {
        private readonly object _sync = new();
        private int _expectedParticipants;
        private int _arrivals;
        private TaskCompletionSource<bool>? _allArrived;
        private TaskCompletionSource<bool>? _release;

        public void Arm(int expectedParticipants)
        {
            lock (_sync)
            {
                _expectedParticipants = expectedParticipants;
                _arrivals = 0;
                _allArrived = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _release = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public async Task WaitForParticipantsAsync(TimeSpan timeout)
        {
            Task wait;
            lock (_sync)
            {
                wait = _allArrived?.Task
                    ?? throw new InvalidOperationException("The request barrier has not been armed.");
            }

            await wait.WaitAsync(timeout);
        }

        public void Release()
        {
            lock (_sync)
            {
                _release?.TrySetResult(true);
            }
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Task? waitForRelease = null;
            lock (_sync)
            {
                if (_expectedParticipants > 0
                    && _arrivals < _expectedParticipants
                    && command.CommandText.Contains("AspNetUsers", StringComparison.OrdinalIgnoreCase))
                {
                    _arrivals++;
                    if (_arrivals == _expectedParticipants)
                    {
                        _allArrived!.TrySetResult(true);
                    }

                    waitForRelease = _release!.Task;
                }
            }

            if (waitForRelease is not null)
            {
                await waitForRelease.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class FailCommitAfterResetUpdatesInterceptor : DbTransactionInterceptor
    {
        private int _armed;
        private Guid _userId;
        private Guid _messageId;
        private UserCredentialState? _before;

        public bool IdentityAndOutboxUpdatesVisibleBeforeCommit { get; private set; }

        public void Arm(Guid userId, Guid messageId, UserCredentialState before)
        {
            _userId = userId;
            _messageId = messageId;
            _before = before;
            Interlocked.Exchange(ref _armed, 1);
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) != 1)
            {
                return result;
            }

            var connection = Assert.IsType<SqlConnection>(transaction.Connection);
            var sqlTransaction = Assert.IsType<SqlTransaction>(transaction);
            UserCredentialState? current = null;
            await using (var userCommand = connection.CreateCommand())
            {
                userCommand.Transaction = sqlTransaction;
                userCommand.CommandText = """
                    SELECT [PasswordHash], [SecurityStamp], [ConcurrencyStamp], [EmailConfirmed]
                    FROM [dbo].[AspNetUsers]
                    WHERE [Id] = @UserId;
                    """;
                userCommand.Parameters.Add(new SqlParameter("@UserId", SqlDbType.UniqueIdentifier)
                {
                    Value = _userId
                });
                await using var reader = await userCommand.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    current = new UserCredentialState(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetBoolean(3));
                }
            }

            await using var outboxCommand = connection.CreateCommand();
            outboxCommand.Transaction = sqlTransaction;
            outboxCommand.CommandText = "SELECT [Status] FROM [dbo].[EmailOutbox] WHERE [Id] = @Id;";
            outboxCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier)
            {
                Value = _messageId
            });
            var status = await outboxCommand.ExecuteScalarAsync(cancellationToken);
            IdentityAndOutboxUpdatesVisibleBeforeCommit =
                current is not null
                && _before is not null
                && !SameCredentialValues(_before, current)
                && status is not null
                && status is not DBNull
                && Convert.ToInt16(status, System.Globalization.CultureInfo.InvariantCulture)
                    == (short)EmailOutboxMessageStatus.Cancelled;

            throw new InjectedResetCommitFailureException();
        }

        private static bool SameCredentialValues(
            UserCredentialState first,
            UserCredentialState second) =>
            string.Equals(first.PasswordHash, second.PasswordHash, StringComparison.Ordinal)
            && string.Equals(first.SecurityStamp, second.SecurityStamp, StringComparison.Ordinal)
            && string.Equals(first.ConcurrencyStamp, second.ConcurrencyStamp, StringComparison.Ordinal)
            && first.EmailConfirmed == second.EmailConfirmed;
    }

    private sealed class InjectedResetCommitFailureException : Exception
    {
    }

    private sealed record AntiforgeryState(string RequestToken, string CookiePair);
    private sealed record IntegrationAccount(Guid Id, string Email);
    private sealed record LoginResult(HttpStatusCode StatusCode, string? AuthCookie);
    private sealed record ResetLink(string Email, string Token);
    private sealed record UserCredentialState(
        string PasswordHash,
        string SecurityStamp,
        string ConcurrencyStamp,
        bool EmailConfirmed);
    private sealed record OutboxState(
        Guid Id,
        Guid UserId,
        EmailOutboxMessageType MessageType,
        EmailOutboxMessageStatus Status,
        int Attempts,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset NextAttemptAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string? ActiveDeduplicationKey,
        DateTimeOffset? LeaseExpiresAtUtc,
        byte[]? CredentialGenerationFingerprint,
        byte[] RowVersion);
}
