using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class EmailConfirmationResendOutboxSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task RequestAsync_ReturnsNotEligibleForMissingConfirmedOrInvalidUsers()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var unconfirmedUserId = await CreateUserAsync(database);
        var confirmedUserId = await CreateUserAsync(database, emailConfirmed: true);
        var noEmailUserId = await CreateUserAsync(database, omitEmail: true);
        var invalidEmailUserId = await CreateInvalidEmailUserAsync(database);
        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();

        Assert.Equal(
            EmailConfirmationResendOutboxResult.NotEligible,
            await outbox.RequestAsync(Guid.NewGuid()));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.NotEligible,
            await outbox.RequestAsync(confirmedUserId));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.NotEligible,
            await outbox.RequestAsync(noEmailUserId));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.NotEligible,
            await outbox.RequestAsync(invalidEmailUserId));
        Assert.Empty(await ReadMessagesAsync(database));

        await using var verification = database.CreateVerificationContext();
        Assert.False((await verification.Users.AsNoTracking().SingleAsync(user => user.Id == unconfirmedUserId))
            .EmailConfirmed);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_DoesNotDuplicatePendingOrProcessingMessagesIncludingExpiredLeases()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var pendingUserId = await CreateUserAsync(database);
        var processingUserId = await CreateUserAsync(database);
        var expiredLeaseUserId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);

        await SeedMessageAsync(
            database,
            CreateMessage(pendingUserId, EmailOutboxMessageStatus.Pending, now.AddMinutes(-10)));
        await SeedMessageAsync(
            database,
            CreateMessage(
                processingUserId,
                EmailOutboxMessageStatus.Processing,
                now.AddMinutes(-10),
                leaseExpiresAtUtc: now.AddMinutes(1)));
        await SeedMessageAsync(
            database,
            CreateMessage(
                expiredLeaseUserId,
                EmailOutboxMessageStatus.Processing,
                now.AddMinutes(-10),
                leaseExpiresAtUtc: now.AddMinutes(-1)));

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.AlreadyQueued,
            await outbox.RequestAsync(pendingUserId));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.AlreadyQueued,
            await outbox.RequestAsync(processingUserId));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.AlreadyQueued,
            await outbox.RequestAsync(expiredLeaseUserId));

        Assert.Equal(3, (await ReadMessagesAsync(database)).Count);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_CompletedAndCancelledMessagesRespectCooldown()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var completedCoolingDownUser = await CreateUserAsync(database);
        var completedReadyUser = await CreateUserAsync(database);
        var cancelledCoolingDownUser = await CreateUserAsync(database);
        var cancelledReadyUser = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);

        await SeedMessageAsync(
            database,
            CreateMessage(
                completedCoolingDownUser,
                EmailOutboxMessageStatus.Completed,
                now.AddMinutes(-10),
                completedAtUtc: now.AddMinutes(-1)));
        await SeedMessageAsync(
            database,
            CreateMessage(
                completedReadyUser,
                EmailOutboxMessageStatus.Completed,
                now.AddMinutes(-10),
                completedAtUtc: now.AddMinutes(-3)));
        await SeedMessageAsync(
            database,
            CreateMessage(
                cancelledCoolingDownUser,
                EmailOutboxMessageStatus.Cancelled,
                now.AddMinutes(-1)));
        await SeedMessageAsync(
            database,
            CreateMessage(
                cancelledReadyUser,
                EmailOutboxMessageStatus.Cancelled,
                now.AddMinutes(-3)));

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.CooldownActive,
            await outbox.RequestAsync(completedCoolingDownUser));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.Enqueued,
            await outbox.RequestAsync(completedReadyUser));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.CooldownActive,
            await outbox.RequestAsync(cancelledCoolingDownUser));
        Assert.Equal(
            EmailConfirmationResendOutboxResult.Enqueued,
            await outbox.RequestAsync(cancelledReadyUser));

        var messages = await ReadMessagesAsync(database);
        Assert.Equal(6, messages.Count);
        Assert.Equal(
            2,
            messages.Count(message => message.Status == EmailOutboxMessageStatus.Pending));
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_RejectsCancelledMessageJustBeforeTwoMinuteCooldown()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var existingMessage = CreateMessage(
            userId,
            EmailOutboxMessageStatus.Cancelled,
            now.AddMinutes(-1).AddSeconds(-50));
        await SeedMessageAsync(database, existingMessage);

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.CooldownActive,
            await outbox.RequestAsync(userId));

        var messages = await ReadMessagesAsync(database);
        var persistedMessage = Assert.Single(messages);
        Assert.Equal(existingMessage.Id, persistedMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, persistedMessage.Status);
        Assert.Equal(existingMessage.CreatedAtUtc, persistedMessage.CreatedAtUtc);
        Assert.Equal(existingMessage.Attempts, persistedMessage.Attempts);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_EnqueuesForCancelledMessageJustAfterTwoMinuteCooldown()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var existingMessage = CreateMessage(
            userId,
            EmailOutboxMessageStatus.Cancelled,
            now.AddMinutes(-2).AddSeconds(-10));
        await SeedMessageAsync(database, existingMessage);

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.Enqueued,
            await outbox.RequestAsync(userId));

        var messages = await ReadMessagesAsync(database);
        Assert.Equal(2, messages.Count);
        var persistedMessage = Assert.Single(messages, message => message.Id == existingMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, persistedMessage.Status);
        Assert.Equal(existingMessage.CreatedAtUtc, persistedMessage.CreatedAtUtc);
        var newMessage = Assert.Single(messages, message => message.Id != existingMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, newMessage.Status);
        Assert.Equal(0, newMessage.Attempts);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_RecentCompletionKeepsCooldownEvenWhenMessageWasCreatedEarlier()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var existingMessage = CreateMessage(
            userId,
            EmailOutboxMessageStatus.Completed,
            now.AddMinutes(-10),
            completedAtUtc: now.AddMinutes(-1).AddSeconds(-50));
        await SeedMessageAsync(database, existingMessage);

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.CooldownActive,
            await outbox.RequestAsync(userId));

        var messages = await ReadMessagesAsync(database);
        var persistedMessage = Assert.Single(messages);
        Assert.Equal(existingMessage.Id, persistedMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Completed, persistedMessage.Status);
        Assert.Equal(existingMessage.CreatedAtUtc, persistedMessage.CreatedAtUtc);
        Assert.Equal(existingMessage.CompletedAtUtc, persistedMessage.CompletedAtUtc);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_EnqueuesWhenCompletionIsJustBeforeCooldownLimit()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var existingMessage = CreateMessage(
            userId,
            EmailOutboxMessageStatus.Completed,
            now.AddMinutes(-10),
            completedAtUtc: now.AddMinutes(-2).AddSeconds(-10));
        await SeedMessageAsync(database, existingMessage);

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.Enqueued,
            await outbox.RequestAsync(userId));

        var messages = await ReadMessagesAsync(database);
        Assert.Equal(2, messages.Count);
        var persistedMessage = Assert.Single(messages, message => message.Id == existingMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Completed, persistedMessage.Status);
        Assert.Equal(existingMessage.CreatedAtUtc, persistedMessage.CreatedAtUtc);
        Assert.Equal(existingMessage.CompletedAtUtc, persistedMessage.CompletedAtUtc);
        var newMessage = Assert.Single(messages, message => message.Id != existingMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, newMessage.Status);
        Assert.Equal(0, newMessage.Attempts);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_CountsInitialSignupAndAllStatusesInRollingHour()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await RegisterClientAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var initialMessage = Assert.Single(await ReadMessagesAsync(database));
        await SetTerminalMessageAsync(
            database,
            initialMessage.Id,
            EmailOutboxMessageStatus.Completed,
            now.AddMinutes(-25),
            now.AddMinutes(-20));

        await SeedMessageAsync(
            database,
            CreateMessage(userId, EmailOutboxMessageStatus.Cancelled, now.AddMinutes(-15)));
        await SeedMessageAsync(
            database,
            CreateMessage(
                userId,
                EmailOutboxMessageStatus.Completed,
                now.AddMinutes(-10),
                completedAtUtc: now.AddMinutes(-10)));
        await SeedMessageAsync(
            database,
            CreateMessage(
                userId,
                EmailOutboxMessageStatus.DeadLetter,
                now.AddMinutes(-7),
                attempts: EmailOutboxRetryPolicy.MaximumAttempts));
        await SeedMessageAsync(
            database,
            CreateMessage(userId, EmailOutboxMessageStatus.Cancelled, now.AddMinutes(-3)));

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.HourlyLimitReached,
            await outbox.RequestAsync(userId));

        await SetTerminalMessageAsync(
            database,
            initialMessage.Id,
            EmailOutboxMessageStatus.Completed,
            now.AddMinutes(-61),
            now.AddMinutes(-61));

        Assert.Equal(
            EmailConfirmationResendOutboxResult.Enqueued,
            await outbox.RequestAsync(userId));
        Assert.Equal(6, (await ReadMessagesAsync(database)).Count);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_DeadLetterPreservesHistoryAndReleasesKeyOnlyAfterCooldown()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var coolingDownUserId = await CreateUserAsync(database);
        var readyUserId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var coolingDownDeadLetter = CreateMessage(
            coolingDownUserId,
            EmailOutboxMessageStatus.DeadLetter,
            now.AddMinutes(-1),
            attempts: EmailOutboxRetryPolicy.MaximumAttempts);
        var readyDeadLetter = CreateMessage(
            readyUserId,
            EmailOutboxMessageStatus.DeadLetter,
            now.AddMinutes(-3),
            attempts: EmailOutboxRetryPolicy.MaximumAttempts);
        await SeedMessageAsync(database, coolingDownDeadLetter);
        await SeedMessageAsync(database, readyDeadLetter);

        var outbox = database.Services.GetRequiredService<IEmailConfirmationResendOutbox>();
        Assert.Equal(
            EmailConfirmationResendOutboxResult.CooldownActive,
            await outbox.RequestAsync(coolingDownUserId));
        var coolingDownAfter = await ReadMessageAsync(database, coolingDownDeadLetter.Id);
        Assert.Equal(
            $"email-confirmation:{coolingDownUserId:N}",
            coolingDownAfter.ActiveDeduplicationKey);
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, coolingDownAfter.Status);

        Assert.Equal(
            EmailConfirmationResendOutboxResult.Enqueued,
            await outbox.RequestAsync(readyUserId));
        var oldMessageAfter = await ReadMessageAsync(database, readyDeadLetter.Id);
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, oldMessageAfter.Status);
        Assert.Equal(EmailOutboxRetryPolicy.MaximumAttempts, oldMessageAfter.Attempts);
        Assert.Equal(readyDeadLetter.CreatedAtUtc, oldMessageAfter.CreatedAtUtc);
        Assert.Null(oldMessageAfter.ActiveDeduplicationKey);
        Assert.NotEqual(readyDeadLetter.RowVersion, oldMessageAfter.RowVersion);

        var newMessage = Assert.Single(
            await ReadMessagesAsync(database),
            message => message.UserId == readyUserId && message.Id != readyDeadLetter.Id);
        Assert.NotEqual(readyDeadLetter.Id, newMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, newMessage.Status);
        Assert.Equal(0, newMessage.Attempts);
        Assert.Equal(
            $"email-confirmation:{readyUserId:N}",
            newMessage.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_RollsBackDeadLetterKeyReleaseWhenInsertFails()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var deadLetter = CreateMessage(
            userId,
            EmailOutboxMessageStatus.DeadLetter,
            now.AddMinutes(-3),
            attempts: EmailOutboxRetryPolicy.MaximumAttempts);
        await SeedMessageAsync(database, deadLetter);

        await using (var context = database.CreateVerificationContext())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_EmailOutbox_RejectResendInsert]
                ON [dbo].[EmailOutbox]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Injected resend insert failure.', 1;
                END;
                """);
        }

        await using var verificationContext = database.CreateVerificationContext();
        var outbox = new SqlServerEmailConfirmationResendOutbox(verificationContext);
        var exception = await Assert.ThrowsAsync<SqlException>(() => outbox.RequestAsync(userId));

        Assert.Equal(51000, exception.Number);
        var persisted = await ReadMessageAsync(database, deadLetter.Id);
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, persisted.Status);
        Assert.Equal(EmailOutboxRetryPolicy.MaximumAttempts, persisted.Attempts);
        Assert.Equal(
            $"email-confirmation:{userId:N}",
            persisted.ActiveDeduplicationKey);
        Assert.Single(await ReadMessagesAsync(database));
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_ConcurrentCallsForSameUserCreateOnlyOneMessageWithRcsi()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await database.EnableReadCommittedSnapshotAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        await SeedMessageAsync(
            database,
            CreateMessage(
                userId,
                EmailOutboxMessageStatus.Completed,
                now.AddMinutes(-5),
                completedAtUtc: now.AddMinutes(-5)));

        await using var firstContext = database.CreateVerificationContext();
        await using var secondContext = database.CreateVerificationContext();
        var firstOutbox = new SqlServerEmailConfirmationResendOutbox(firstContext);
        var secondOutbox = new SqlServerEmailConfirmationResendOutbox(secondContext);

        var results = await Task.WhenAll(
            firstOutbox.RequestAsync(userId),
            secondOutbox.RequestAsync(userId));

        Assert.Single(results, result => result == EmailConfirmationResendOutboxResult.Enqueued);
        Assert.Single(results, result => result == EmailConfirmationResendOutboxResult.AlreadyQueued);
        var messages = await ReadMessagesAsync(database);
        Assert.Equal(2, messages.Count);
        Assert.Single(
            messages,
            message => message.ActiveDeduplicationKey == $"email-confirmation:{userId:N}");
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_RacesSafelyWithWorkerCompletionUnderRcsi()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await database.EnableReadCommittedSnapshotAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var pending = CreateMessage(userId, EmailOutboxMessageStatus.Pending, now.AddMinutes(-5));
        await SeedMessageAsync(database, pending);

        await using var workerContext = database.CreateVerificationContext();
        var workerStore = new SqlServerEmailOutboxStore(workerContext);
        var claim = Assert.Single(await workerStore.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));
        await using var resendContext = database.CreateVerificationContext();
        var resendOutbox = new SqlServerEmailConfirmationResendOutbox(resendContext);

        var requestTask = resendOutbox.RequestAsync(userId);
        var completionTask = workerStore.CompleteAsync(claim);
        await Task.WhenAll(requestTask, completionTask);

        Assert.True(await completionTask);
        Assert.True(await requestTask is
            EmailConfirmationResendOutboxResult.AlreadyQueued
            or EmailConfirmationResendOutboxResult.CooldownActive);
        var messages = await ReadMessagesAsync(database);
        Assert.Single(messages);
        Assert.Equal(EmailOutboxMessageStatus.Completed, messages[0].Status);
        Assert.Null(messages[0].ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_RacesSafelyWithWorkerDeadLetterUnderRcsi()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await database.EnableReadCommittedSnapshotAsync();
        var userId = await CreateUserAsync(database);
        var now = await GetSqlUtcNowAsync(database);
        var pending = CreateMessage(userId, EmailOutboxMessageStatus.Pending, now.AddMinutes(-5));
        await SeedMessageAsync(database, pending);

        await using var workerContext = database.CreateVerificationContext();
        var workerStore = new SqlServerEmailOutboxStore(workerContext);
        var claim = Assert.Single(await workerStore.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));
        await using var resendContext = database.CreateVerificationContext();
        var resendOutbox = new SqlServerEmailConfirmationResendOutbox(resendContext);

        var requestTask = resendOutbox.RequestAsync(userId);
        var deadLetterTask = workerStore.DeadLetterAsync(claim);
        await Task.WhenAll(requestTask, deadLetterTask);
        var requestResult = await requestTask;

        Assert.True(await deadLetterTask);
        Assert.True(requestResult is
            EmailConfirmationResendOutboxResult.AlreadyQueued
            or EmailConfirmationResendOutboxResult.Enqueued);
        var messages = await ReadMessagesAsync(database);
        Assert.Equal(
            requestResult == EmailConfirmationResendOutboxResult.Enqueued ? 2 : 1,
            messages.Count);
        Assert.Equal(
            requestResult == EmailConfirmationResendOutboxResult.Enqueued
                ? 1
                : 0,
            messages.Count(message => message.Status == EmailOutboxMessageStatus.Pending));
        Assert.Single(
            messages,
            message => message.ActiveDeduplicationKey == $"email-confirmation:{userId:N}");
        Assert.Contains(messages, message => message.Status == EmailOutboxMessageStatus.DeadLetter);
    }

    [SqlServerIntegrationFact]
    public async Task RequestAsync_WhenApplicationLockTimesOutThrowsWithoutEnqueueing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        await using var lockContext = database.CreateVerificationContext();
        var connection = (SqlConnection)lockContext.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                DECLARE @LockResult int;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 0,
                    @DbPrincipal = 'public';
                SELECT @LockResult;
                """;
            command.Parameters.Add(new SqlParameter(
                "@Resource",
                SqlDbType.NVarChar,
                255)
            {
                Value = $"EmailConfirmationResend:{userId:N}"
            });
            Assert.True((int)(await command.ExecuteScalarAsync())! >= 0);
        }

        await using var resendContext = database.CreateVerificationContext();
        var outbox = new SqlServerEmailConfirmationResendOutbox(resendContext);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => outbox.RequestAsync(userId));
        Assert.Contains("lock could not be acquired", exception.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
        Assert.Empty(await ReadMessagesAsync(database));
    }

    private static async Task<Guid> CreateUserAsync(
        SqlServerTestDatabase database,
        string? email = null,
        bool emailConfirmed = false,
        bool omitEmail = false)
    {
        var userName = $"resend-outbox-{Guid.NewGuid():N}@example.test";
        if (omitEmail)
        {
            var userId = Guid.NewGuid();
            await using var context = database.CreateVerificationContext();
            context.Users.Add(new XaquiamuniUser
            {
                Id = userId,
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                EmailConfirmed = emailConfirmed
            });
            await context.SaveChangesAsync();
            return userId;
        }

        var user = new XaquiamuniUser
        {
            UserName = userName,
            Email = email ?? userName,
            EmailConfirmed = emailConfirmed
        };
        var result = await database.Services
            .GetRequiredService<UserManager<XaquiamuniUser>>()
            .CreateAsync(user, "Resend Outbox Test 987!");
        Assert.True(result.Succeeded);
        return user.Id;
    }

    private static async Task<Guid> CreateInvalidEmailUserAsync(SqlServerTestDatabase database)
    {
        var userId = Guid.NewGuid();
        var userName = $"invalid-email-{Guid.NewGuid():N}";
        await using var context = database.CreateVerificationContext();
        context.Users.Add(new XaquiamuniUser
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = "invalid-email",
            NormalizedEmail = "INVALID-EMAIL",
            EmailConfirmed = false
        });
        await context.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> RegisterClientAsync(SqlServerTestDatabase database)
    {
        var email = $"resend-outbox-registration-{Guid.NewGuid():N}@example.test";
        var result = await database.Services
            .GetRequiredService<ICadastrarCliente>()
            .ExecuteAsync(new CadastroClienteInput
            {
                NomeCompleto = "Cliente Teste",
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

    private static async Task SeedMessageAsync(
        SqlServerTestDatabase database,
        EmailOutboxMessage message)
    {
        await using var context = database.CreateVerificationContext();
        context.EmailOutboxMessages.Add(message);
        await context.SaveChangesAsync();
    }

    private static EmailOutboxMessage CreateMessage(
        Guid userId,
        EmailOutboxMessageStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc = null,
        DateTimeOffset? leaseExpiresAtUtc = null,
        int attempts = 1,
        bool activeDeduplicationKey = false)
    {
        var requiresActiveKey = activeDeduplicationKey
            || status is EmailOutboxMessageStatus.Pending
                or EmailOutboxMessageStatus.Processing
                or EmailOutboxMessageStatus.DeadLetter;
        return new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            status,
            status == EmailOutboxMessageStatus.Pending ? 0 : attempts,
            createdAtUtc,
            createdAtUtc,
            completedAtUtc,
            requiresActiveKey ? $"email-confirmation:{userId:N}" : null,
            leaseExpiresAtUtc);
    }

    private static async Task SetTerminalMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId,
        EmailOutboxMessageStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[EmailOutbox]
            SET [Status] = {(short)status},
                [CreatedAtUtc] = {createdAtUtc},
                [NextAttemptAtUtc] = {createdAtUtc},
                [CompletedAtUtc] = {completedAtUtc},
                [LeaseExpiresAtUtc] = NULL,
                [ActiveDeduplicationKey] = NULL
            WHERE [Id] = {messageId};
            """);
    }

    private static async Task<List<EmailOutboxMessage>> ReadMessagesAsync(
        SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages.AsNoTracking().ToListAsync();
    }

    private static async Task<EmailOutboxMessage> ReadMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Id == messageId);
    }

    private static async Task<DateTimeOffset> GetSqlUtcNowAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');";
        return (DateTimeOffset)(await command.ExecuteScalarAsync())!;
    }
}
