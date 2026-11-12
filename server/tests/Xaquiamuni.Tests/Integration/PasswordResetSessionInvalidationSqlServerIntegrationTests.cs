using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class PasswordResetSessionInvalidationSqlServerIntegrationTests
{
    private const string OriginalPassword = "Original Password 987!";
    private const string NewPassword = "Replacement Password 987!";
    private const string RejectedPassword = "weak";
    private const string PasswordResetPurpose = "ResetPassword";

    [SqlServerIntegrationFact]
    public async Task ScenarioA_ResetInvalidatesTheAuthenticatedSessionAndCancelsRecovery()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        var account = await CreateConfirmedAccountAsync(database);
        using var client = CreateClient(factory);

        var login = await LoginAsync(client, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.NotNull(login.AuthCookie);
        AssertCookieSecurityStamp(factory, login.AuthCookie!, account.Id,
            (await ReadUserStateAsync(database, account.Id)).SecurityStamp);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(client, login.AuthCookie!)).StatusCode);

        var resetToken = await RequestAndGenerateTokenAsync(database, account.Id, account.Email);
        var resetResult = await ResetPasswordAsync(
            database,
            account.Email,
            resetToken.Token,
            NewPassword);

        Assert.Equal(PasswordResetStatus.Succeeded, resetResult.Status);
        using var staleResponse = await GetMeAsync(client, login.AuthCookie!);
        AssertUnauthenticated(staleResponse);
        Assert.False(await VerifyResetTokenAsync(database, account.Id, resetToken.Token));
        Assert.Equal(
            EmailOutboxMessageStatus.Cancelled,
            (await ReadMessageAsync(database, resetToken.MessageId)).Status);
        Assert.True(await CheckPasswordAsync(database, account.Id, NewPassword));
        Assert.False(await CheckPasswordAsync(database, account.Id, OriginalPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioB_ResetInvalidatesEveryIndependentSessionForTheUser()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        var account = await CreateConfirmedAccountAsync(database);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);

        var firstLogin = await LoginAsync(firstClient, account.Email, OriginalPassword);
        var secondLogin = await LoginAsync(secondClient, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondLogin.StatusCode);
        Assert.NotNull(firstLogin.AuthCookie);
        Assert.NotNull(secondLogin.AuthCookie);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(firstClient, firstLogin.AuthCookie!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(secondClient, secondLogin.AuthCookie!)).StatusCode);

        var resetToken = await RequestAndGenerateTokenAsync(database, account.Id, account.Email);
        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await ResetPasswordAsync(database, account.Email, resetToken.Token, NewPassword)).Status);

        using var firstStaleSession = await GetMeAsync(firstClient, firstLogin.AuthCookie!);
        using var secondStaleSession = await GetMeAsync(secondClient, secondLogin.AuthCookie!);
        AssertUnauthenticated(firstStaleSession);
        AssertUnauthenticated(secondStaleSession);
        Assert.False(await VerifyResetTokenAsync(database, account.Id, resetToken.Token));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioC_NewLoginAfterResetUsesNewCredentialsAndKeepsAccountState()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        var account = await CreateConfirmedAccountAsync(database);
        using var client = CreateClient(factory);
        var oldLogin = await LoginAsync(client, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, oldLogin.StatusCode);
        Assert.NotNull(oldLogin.AuthCookie);

        var resetToken = await RequestAndGenerateTokenAsync(database, account.Id, account.Email);
        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await ResetPasswordAsync(database, account.Email, resetToken.Token, NewPassword)).Status);
        var currentUser = await ReadUserStateAsync(database, account.Id);

        var rejectedLogin = await LoginAsync(client, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedLogin.StatusCode);
        Assert.Null(rejectedLogin.AuthCookie);

        var newLogin = await LoginAsync(client, account.Email, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, newLogin.StatusCode);
        Assert.NotNull(newLogin.AuthCookie);
        AssertCookieSecurityStamp(factory, newLogin.AuthCookie!, account.Id, currentUser.SecurityStamp);

        using var newSession = await GetMeAsync(client, newLogin.AuthCookie!);
        Assert.Equal(HttpStatusCode.OK, newSession.StatusCode);
        Assert.Equal(account.Id, await ReadCurrentUserIdAsync(newSession));
        using var oldSession = await GetMeAsync(client, oldLogin.AuthCookie!);
        AssertUnauthenticated(oldSession);

        Assert.True(currentUser.EmailConfirmed);
        Assert.True(await IsInCustomerRoleAsync(database, account.Id));
        Assert.False(await VerifyResetTokenAsync(database, account.Id, resetToken.Token));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioD_ResetOfOneUserLeavesAnotherUsersSessionAndOutboxUntouched()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        var accountA = await CreateConfirmedAccountAsync(database);
        var accountB = await CreateConfirmedAccountAsync(database);
        using var clientA = CreateClient(factory);
        using var clientB = CreateClient(factory);

        var loginA = await LoginAsync(clientA, accountA.Email, OriginalPassword);
        var loginB = await LoginAsync(clientB, accountB.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, loginA.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, loginB.StatusCode);
        Assert.NotNull(loginA.AuthCookie);
        Assert.NotNull(loginB.AuthCookie);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(clientA, loginA.AuthCookie!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(clientB, loginB.AuthCookie!)).StatusCode);

        var resetTokenA = await RequestAndGenerateTokenAsync(database, accountA.Id, accountA.Email);
        var messageB = await RequestAndGenerateTokenAsync(database, accountB.Id, accountB.Email);
        var userBBefore = await ReadUserStateAsync(database, accountB.Id);
        var messageBBefore = await ReadMessageAsync(database, messageB.MessageId);

        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await ResetPasswordAsync(database, accountA.Email, resetTokenA.Token, NewPassword)).Status);

        using var staleA = await GetMeAsync(clientA, loginA.AuthCookie!);
        using var activeB = await GetMeAsync(clientB, loginB.AuthCookie!);
        AssertUnauthenticated(staleA);
        Assert.Equal(HttpStatusCode.OK, activeB.StatusCode);
        Assert.Equal(accountB.Id, await ReadCurrentUserIdAsync(activeB));
        AssertUserStateEqual(userBBefore, await ReadUserStateAsync(database, accountB.Id));
        AssertOutboxStateEqual(messageBBefore, await ReadMessageAsync(database, messageB.MessageId));
        Assert.Equal(
            EmailOutboxMessageStatus.Cancelled,
            (await ReadMessageAsync(database, resetTokenA.MessageId)).Status);
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioE_RejectedResetsPreserveTheSessionCredentialsAndOutbox()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        var account = await CreateConfirmedAccountAsync(database);
        using var client = CreateClient(factory);
        var login = await LoginAsync(client, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.NotNull(login.AuthCookie);

        var resetToken = await RequestAndGenerateTokenAsync(database, account.Id, account.Email);
        var userBefore = await ReadUserStateAsync(database, account.Id);
        var messageBefore = await ReadMessageAsync(database, resetToken.MessageId);

        var invalidTokenResult = await ResetPasswordAsync(
            database,
            account.Email,
            "not-a-valid-reset-token",
            NewPassword);
        Assert.Equal(PasswordResetStatus.InvalidRequest, invalidTokenResult.Status);
        using (var afterInvalidToken = await GetMeAsync(client, login.AuthCookie!))
        {
            Assert.Equal(HttpStatusCode.OK, afterInvalidToken.StatusCode);
        }
        AssertUserStateEqual(userBefore, await ReadUserStateAsync(database, account.Id));
        AssertOutboxStateEqual(messageBefore, await ReadMessageAsync(database, resetToken.MessageId));

        var invalidPasswordResult = await ResetPasswordAsync(
            database,
            account.Email,
            resetToken.Token,
            RejectedPassword);
        Assert.Equal(PasswordResetStatus.InvalidPassword, invalidPasswordResult.Status);
        using (var afterInvalidPassword = await GetMeAsync(client, login.AuthCookie!))
        {
            Assert.Equal(HttpStatusCode.OK, afterInvalidPassword.StatusCode);
        }
        AssertUserStateEqual(userBefore, await ReadUserStateAsync(database, account.Id));
        AssertOutboxStateEqual(messageBefore, await ReadMessageAsync(database, resetToken.MessageId));
        Assert.True(await VerifyResetTokenAsync(database, account.Id, resetToken.Token));
        Assert.True(await CheckPasswordAsync(database, account.Id, OriginalPassword));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioF_RollbackAfterIdentityAndOutboxUpdatesPreservesTheExistingSession()
    {
        var commitInterceptor = new FailCommitAfterResetUpdatesInterceptor();
        await using var database = await SqlServerTestDatabase.CreateAsync(
            dbContextInterceptor: commitInterceptor);
        using var factory = CreateFactory(database);
        var account = await CreateConfirmedAccountAsync(database);
        using var client = CreateClient(factory);
        var login = await LoginAsync(client, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.NotNull(login.AuthCookie);

        var resetToken = await RequestAndGenerateTokenAsync(database, account.Id, account.Email);
        var userBefore = await ReadUserStateAsync(database, account.Id);
        var messageBefore = await ReadMessageAsync(database, resetToken.MessageId);

        await using var resetScope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var resetServices = resetScope.ServiceProvider;
        var dbContext = resetServices.GetRequiredService<XaquiamuniDbContext>();
        var trackedMessage = await dbContext.EmailOutboxMessages
            .SingleAsync(message => message.Id == resetToken.MessageId);
        commitInterceptor.Arm(account.Id, resetToken.MessageId, userBefore);

        await Assert.ThrowsAsync<InjectedResetCommitFailureException>(async () =>
            await resetServices.GetRequiredService<IPasswordResetService>()
                .ResetPasswordAsync(account.Email, resetToken.Token, NewPassword));

        Assert.True(commitInterceptor.IdentityAndOutboxUpdatesVisibleBeforeCommit);
        Assert.Equal(EntityState.Detached, dbContext.Entry(trackedMessage).State);
        await dbContext.SaveChangesAsync();

        AssertUserStateEqual(userBefore, await ReadUserStateAsync(database, account.Id));
        AssertOutboxStateEqual(messageBefore, await ReadMessageAsync(database, resetToken.MessageId));
        Assert.True(await VerifyResetTokenAsync(database, account.Id, resetToken.Token));
        using var existingSession = await GetMeAsync(client, login.AuthCookie!);
        Assert.Equal(HttpStatusCode.OK, existingSession.StatusCode);
        Assert.Null(GetAuthCookieSetBy(existingSession));
    }

    [SqlServerIntegrationFact]
    public async Task ScenarioG_PostResetLoginStillExpiresAtTheOriginalEightHourAbsoluteLimit()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database, clock);
        var account = await CreateConfirmedAccountAsync(database);
        using var client = CreateClient(factory);
        var oldLogin = await LoginAsync(client, account.Email, OriginalPassword);
        Assert.Equal(HttpStatusCode.NoContent, oldLogin.StatusCode);
        Assert.NotNull(oldLogin.AuthCookie);

        var resetToken = await RequestAndGenerateTokenAsync(database, account.Id, account.Email);
        Assert.Equal(
            PasswordResetStatus.Succeeded,
            (await ResetPasswordAsync(database, account.Email, resetToken.Token, NewPassword)).Status);

        using (var oldSession = await GetMeAsync(client, oldLogin.AuthCookie!))
        {
            AssertUnauthenticated(oldSession);
        }

        var newLogin = await LoginAsync(client, account.Email, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, newLogin.StatusCode);
        Assert.NotNull(newLogin.AuthCookie);
        var initialTicket = GetTicket(factory, newLogin.AuthCookie!);
        var initialIssued = Assert.IsType<DateTimeOffset>(initialTicket.Properties.IssuedUtc);
        var initialExpiry = Assert.IsType<DateTimeOffset>(initialTicket.Properties.ExpiresUtc);
        Assert.Equal(TimeSpan.FromHours(8), initialExpiry - initialIssued);
        AssertCookieSecurityStamp(
            factory,
            newLogin.AuthCookie!,
            account.Id,
            (await ReadUserStateAsync(database, account.Id)).SecurityStamp);

        using (var activeSession = await GetMeAsync(client, newLogin.AuthCookie!))
        {
            Assert.Equal(HttpStatusCode.OK, activeSession.StatusCode);
        }

        clock.Advance(TimeSpan.FromHours(7));
        using (var validatedSession = await GetMeAsync(client, newLogin.AuthCookie!))
        {
            Assert.Equal(HttpStatusCode.OK, validatedSession.StatusCode);
            Assert.Null(GetAuthCookieSetBy(validatedSession));
        }

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromTicks(1));
        using var expiredSession = await GetMeAsync(client, newLogin.AuthCookie!);
        AssertUnauthenticated(expiredSession);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        SqlServerTestDatabase database,
        AdjustableTimeProvider? clock = null)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString,
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<XaquiamuniDbContext>(
                    options => options.UseSqlServer(connectionString));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                if (clock is not null)
                {
                    services.PostConfigure<CookieAuthenticationOptions>(
                        AuthenticationConfiguration.CookieScheme,
                        options => options.TimeProvider = clock);
                    services.PostConfigure<SecurityStampValidatorOptions>(
                        options => options.TimeProvider = clock);
                }
            });
        });

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
            factory.Dispose();
            throw new InvalidOperationException(
                "The HTTP test host is not configured for its isolated local SQL test database.");
        }

        return factory;
    }

    private static async Task<TestAccount> CreateConfirmedAccountAsync(SqlServerTestDatabase database)
    {
        await using var scope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var email = $"password-reset-session-{Guid.NewGuid():N}@example.test";
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user, OriginalPassword);
        Assert.True(createResult.Succeeded, "The test account could not be created.");
        var roleResult = await userManager.AddToRoleAsync(user, XaquiamuniRoles.Cliente);
        Assert.True(roleResult.Succeeded, "The test account role could not be assigned.");
        return new TestAccount(user.Id, email);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    private static async Task<LoginResult> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        using var csrfResponse = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        using var csrfDocument = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        var requestToken = csrfDocument.RootElement.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestToken), "The antiforgery request token is missing.");
        var csrfCookie = FindCookiePair(
            csrfResponse,
            AntiforgeryConfiguration.CookieName);

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.LoginEndpoint)
        {
            Content = JsonContent.Create(new LoginClientRequest
            {
                Email = email,
                Senha = password
            })
        };
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);
        request.Headers.Add("Cookie", csrfCookie);
        using var response = await client.SendAsync(request);
        var authCookie = TryFindCookiePair(response, AuthenticationConfiguration.CookieName);
        return new LoginResult(response.StatusCode, authCookie);
    }

    private static async Task<HttpResponseMessage> GetMeAsync(HttpClient client, string authCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AuthEndpoints.MeEndpoint);
        request.Headers.Add("Cookie", authCookie);
        return await client.SendAsync(request);
    }

    private static void AssertUnauthenticated(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Content.Headers.ContentType?.MediaType?
            .Contains("html", StringComparison.OrdinalIgnoreCase) ?? false);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Guid> ReadCurrentUserIdAsync(HttpResponseMessage response)
    {
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return Assert.IsType<JsonElement>(document?.RootElement)
            .GetProperty("userId")
            .GetGuid();
    }

    private static async Task<ResetToken> RequestAndGenerateTokenAsync(
        SqlServerTestDatabase database,
        Guid userId,
        string email)
    {
        await using (var requestScope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope())
        {
            var requestResult = await requestScope.ServiceProvider
                .GetRequiredService<ISolicitarRecuperacaoSenha>()
                .ExecuteAsync(email);
            Assert.Equal(SolicitarRecuperacaoSenhaStatus.RequestProcessed, requestResult.Status);
        }

        var message = await ReadSinglePasswordResetMessageAsync(database, userId);
        var fingerprint = message.CredentialGenerationFingerprint
            ?? throw new Xunit.Sdk.XunitException("The password reset generation fingerprint is missing.");
        try
        {
            await using var tokenScope = database.Services.GetRequiredService<IServiceScopeFactory>()
                .CreateAsyncScope();
            var tokenResult = await tokenScope.ServiceProvider
                .GetRequiredService<IPasswordResetService>()
                .GeneratePasswordResetTokenForUserAsync(userId, fingerprint);
            Assert.Equal(PasswordResetTokenStatus.Generated, tokenResult.Status);
            Assert.NotNull(tokenResult.Token);
            return new ResetToken(message.Id, tokenResult.Token!);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fingerprint);
        }
    }

    private static async Task<PasswordResetResult> ResetPasswordAsync(
        SqlServerTestDatabase database,
        string email,
        string token,
        string password)
    {
        await using var scope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
            .ResetPasswordAsync(email, token, password);
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

    private static async Task<OutboxState> ReadSinglePasswordResetMessageAsync(
        SqlServerTestDatabase database,
        Guid userId)
    {
        await using var context = database.CreateVerificationContext();
        return ToOutboxState(await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message =>
                message.UserId == userId
                && message.MessageType == EmailOutboxMessageType.PasswordReset));
    }

    private static async Task<OutboxState> ReadMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        return ToOutboxState(await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Id == messageId));
    }

    private static OutboxState ToOutboxState(EmailOutboxMessage message) =>
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
            (byte[])message.RowVersion.Clone());

    private static bool SameOutboxState(OutboxState first, OutboxState second) =>
        first.Id == second.Id
        && first.UserId == second.UserId
        && first.MessageType == second.MessageType
        && first.Status == second.Status
        && first.Attempts == second.Attempts
        && first.CreatedAtUtc == second.CreatedAtUtc
        && first.NextAttemptAtUtc == second.NextAttemptAtUtc
        && first.CompletedAtUtc == second.CompletedAtUtc
        && string.Equals(first.ActiveDeduplicationKey, second.ActiveDeduplicationKey, StringComparison.Ordinal)
        && first.LeaseExpiresAtUtc == second.LeaseExpiresAtUtc
        && NullableBytesEqual(first.CredentialGenerationFingerprint, second.CredentialGenerationFingerprint)
        && BytesEqual(first.RowVersion, second.RowVersion);

    private static void AssertOutboxStateEqual(OutboxState expected, OutboxState actual) =>
        Assert.True(SameOutboxState(expected, actual), "The Outbox state changed unexpectedly.");

    private static bool NullableBytesEqual(byte[]? first, byte[]? second) =>
        first is null ? second is null : second is not null && BytesEqual(first, second);

    private static bool BytesEqual(byte[]? first, byte[]? second) =>
        first is not null
        && second is not null
        && first.Length == second.Length
        && CryptographicOperations.FixedTimeEquals(first, second);

    private static void AssertUserStateEqual(
        UserCredentialState expected,
        UserCredentialState actual) =>
        Assert.True(SameUserState(expected, actual), "The user's credential state changed unexpectedly.");

    private static bool SameUserState(UserCredentialState first, UserCredentialState second) =>
        string.Equals(first.PasswordHash, second.PasswordHash, StringComparison.Ordinal)
        && string.Equals(first.SecurityStamp, second.SecurityStamp, StringComparison.Ordinal)
        && string.Equals(first.ConcurrencyStamp, second.ConcurrencyStamp, StringComparison.Ordinal)
        && first.EmailConfirmed == second.EmailConfirmed;

    private static async Task<bool> IsInCustomerRoleAsync(SqlServerTestDatabase database, Guid userId)
    {
        await using var scope = database.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await userManager.IsInRoleAsync(user, XaquiamuniRoles.Cliente);
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
            PasswordResetPurpose,
            token);
    }

    private static void AssertCookieSecurityStamp(
        WebApplicationFactory<Program> factory,
        string authCookie,
        Guid userId,
        string expectedSecurityStamp)
    {
        var ticket = GetTicket(factory, authCookie);
        var identityOptions = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var identity = ticket.Principal.Identity as ClaimsIdentity;
        Assert.NotNull(identity);
        var userIdClaim = identity.FindFirst(identityOptions.ClaimsIdentity.UserIdClaimType)?.Value;
        Assert.True(
            string.Equals(userId.ToString(), userIdClaim, StringComparison.Ordinal),
            "The authentication cookie user identifier did not match.");
        var stampClaim = identity.FindFirst(identityOptions.ClaimsIdentity.SecurityStampClaimType)?.Value;
        Assert.True(
            string.Equals(expectedSecurityStamp, stampClaim, StringComparison.Ordinal),
            "The authentication cookie did not contain the current credential generation.");
    }

    private static AuthenticationTicket GetTicket(
        WebApplicationFactory<Program> factory,
        string authCookie)
    {
        var protectedTicket = authCookie.Split('=', 2)[1];
        var ticketDataFormat = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationConfiguration.CookieScheme)
            .TicketDataFormat;
        return ticketDataFormat.Unprotect(protectedTicket)
            ?? throw new Xunit.Sdk.XunitException("The authentication ticket could not be unprotected.");
    }

    private static string FindCookiePair(HttpResponseMessage response, string name) =>
        TryFindCookiePair(response, name)
        ?? throw new Xunit.Sdk.XunitException("The expected HTTP cookie was not issued.");

    private static string? TryFindCookiePair(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var cookie = cookies.FirstOrDefault(value =>
            value.StartsWith(name + "=", StringComparison.Ordinal));
        return cookie?.Split(';', 2)[0];
    }

    private static string? GetAuthCookieSetBy(HttpResponseMessage response) =>
        TryFindCookiePair(response, AuthenticationConfiguration.CookieName);

    private sealed class FailCommitAfterResetUpdatesInterceptor : DbTransactionInterceptor
    {
        private readonly object _sync = new();
        private Guid _userId;
        private Guid _messageId;
        private UserCredentialState? _before;
        private int _armed;

        public bool IdentityAndOutboxUpdatesVisibleBeforeCommit { get; private set; }

        public void Arm(Guid userId, Guid messageId, UserCredentialState before)
        {
            lock (_sync)
            {
                _userId = userId;
                _messageId = messageId;
                _before = before;
            }

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

            Guid userId;
            Guid messageId;
            UserCredentialState before;
            lock (_sync)
            {
                userId = _userId;
                messageId = _messageId;
                before = _before
                    ?? throw new InvalidOperationException("The reset rollback interceptor was not armed.");
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
                    Value = userId
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
                Value = messageId
            });
            var status = await outboxCommand.ExecuteScalarAsync(cancellationToken);
            IdentityAndOutboxUpdatesVisibleBeforeCommit =
                current is not null
                && !SameUserState(before, current)
                && status is not null
                && status is not DBNull
                && Convert.ToInt16(status, CultureInfo.InvariantCulture)
                    == (short)EmailOutboxMessageStatus.Cancelled;

            throw new InjectedResetCommitFailureException();
        }
    }

    private sealed class InjectedResetCommitFailureException : Exception
    {
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed record TestAccount(Guid Id, string Email);
    private sealed record LoginResult(HttpStatusCode StatusCode, string? AuthCookie);
    private sealed record ResetToken(Guid MessageId, string Token);
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
