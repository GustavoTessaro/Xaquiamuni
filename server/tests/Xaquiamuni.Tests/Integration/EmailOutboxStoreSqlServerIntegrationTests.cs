using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class EmailOutboxStoreSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ClaimDueAsync_WithEmptyExclusionListClaimsOneMessage()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var message = CreatePendingMessage(userId);
        await SeedMessageAsync(database, message);

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);

        var claim = Assert.Single(await store.ClaimDueAsync(
            batchSize: 1,
            TimeSpan.FromMinutes(2),
            excludedMessageIds: Array.Empty<Guid>()));

        Assert.Equal(message.Id, claim.Id);
        Assert.Equal(1, claim.Attempts);
    }

    [SqlServerIntegrationFact]
    public async Task ClaimDueAsync_ExcludesOneEligibleMessage()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var excludedUserId = await CreateUserAsync(database);
        var eligibleUserId = await CreateUserAsync(database);
        var excludedMessage = CreatePendingMessage(excludedUserId);
        var eligibleMessage = CreatePendingMessage(eligibleUserId);
        await SeedMessageAsync(database, excludedMessage);
        await SeedMessageAsync(database, eligibleMessage);

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);

        var claim = Assert.Single(await store.ClaimDueAsync(
            batchSize: 2,
            TimeSpan.FromMinutes(2),
            excludedMessageIds: [excludedMessage.Id]));

        Assert.Equal(eligibleMessage.Id, claim.Id);

        await using var verification = database.CreateVerificationContext();
        var excludedPersisted = await verification.EmailOutboxMessages
            .SingleAsync(message => message.Id == excludedMessage.Id);
        Assert.Equal(EmailOutboxMessageStatus.Pending, excludedPersisted.Status);
    }

    [SqlServerIntegrationFact]
    public async Task ClaimDueAsync_ExcludesMultipleEligibleMessages()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var firstUserId = await CreateUserAsync(database);
        var secondUserId = await CreateUserAsync(database);
        var eligibleUserId = await CreateUserAsync(database);
        var firstExcluded = CreatePendingMessage(firstUserId);
        var secondExcluded = CreatePendingMessage(secondUserId);
        var eligibleMessage = CreatePendingMessage(eligibleUserId);
        await SeedMessageAsync(database, firstExcluded);
        await SeedMessageAsync(database, secondExcluded);
        await SeedMessageAsync(database, eligibleMessage);

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);

        var claim = Assert.Single(await store.ClaimDueAsync(
            batchSize: 3,
            TimeSpan.FromMinutes(2),
            excludedMessageIds: [firstExcluded.Id, secondExcluded.Id]));

        Assert.Equal(eligibleMessage.Id, claim.Id);

        await using var verification = database.CreateVerificationContext();
        var excludedStatuses = await verification.EmailOutboxMessages
            .Where(message => message.Id == firstExcluded.Id || message.Id == secondExcluded.Id)
            .Select(message => message.Status)
            .ToListAsync();
        Assert.All(excludedStatuses, status => Assert.Equal(EmailOutboxMessageStatus.Pending, status));
    }

    [SqlServerIntegrationFact]
    public async Task ClaimDueAsync_DoesNotReclaimRetriedPendingMessageWhenExcluded()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var message = CreatePendingMessage(userId);
        await SeedMessageAsync(database, message);

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);
        var claim = Assert.Single(await store.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));
        Assert.True(await store.RetryAsync(claim, jitterSample: 1));

        await using (var makeRetryEligible = database.CreateVerificationContext())
        {
            await makeRetryEligible.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [dbo].[EmailOutbox] SET [NextAttemptAtUtc] = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE [Id] = {message.Id}");
        }

        var claims = await store.ClaimDueAsync(
            batchSize: 1,
            TimeSpan.FromMinutes(2),
            excludedMessageIds: [message.Id]);

        Assert.Empty(claims);
        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.Pending, persisted.Status);
        Assert.Equal(1, persisted.Attempts);
    }

    [SqlServerIntegrationFact]
    public async Task ClaimDueAsync_WithReadCommittedSnapshot_ClaimsMessageOnlyOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await database.EnableReadCommittedSnapshotAsync();
        var userId = await CreateUserAsync(database);

        await using (var seed = database.CreateVerificationContext())
        {
            seed.EmailOutboxMessages.Add(CreatePendingMessage(userId));
            await seed.SaveChangesAsync();
        }

        await using var firstContext = database.CreateVerificationContext();
        await using var secondContext = database.CreateVerificationContext();
        var firstStore = new SqlServerEmailOutboxStore(firstContext);
        var secondStore = new SqlServerEmailOutboxStore(secondContext);

        var results = await Task.WhenAll(
            firstStore.ClaimDueAsync(
                batchSize: 1,
                TimeSpan.FromMinutes(2),
                excludedMessageIds: Array.Empty<Guid>()),
            secondStore.ClaimDueAsync(
                batchSize: 1,
                TimeSpan.FromMinutes(2),
                excludedMessageIds: Array.Empty<Guid>()));

        var claim = Assert.Single(results.SelectMany(result => result));
        Assert.Equal(userId, claim.UserId);
        Assert.Equal(1, claim.Attempts);

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.Processing, persisted.Status);
        Assert.Equal(1, persisted.Attempts);
    }

    [SqlServerIntegrationFact]
    public async Task ClaimDueAsync_ReclaimsExpiredLeaseAndRejectsStaleRowVersion()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var message = new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            EmailOutboxMessageStatus.Processing,
            attempts: 1,
            createdAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            nextAttemptAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            activeDeduplicationKey: $"email-confirmation:{userId:N}",
            leaseExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));

        await using (var seed = database.CreateVerificationContext())
        {
            seed.EmailOutboxMessages.Add(message);
            await seed.SaveChangesAsync();
        }

        var staleClaim = new EmailOutboxClaim(
            message.Id,
            message.UserId,
            message.Attempts,
            message.LeaseExpiresAtUtc!.Value,
            message.RowVersion);

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);
        var currentClaim = Assert.Single(await store.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));

        Assert.Equal(2, currentClaim.Attempts);
        Assert.NotEqual(staleClaim.RowVersion.ToArray(), currentClaim.RowVersion.ToArray());
        Assert.False(await store.CompleteAsync(staleClaim));
        Assert.False(await store.RetryAsync(staleClaim, jitterSample: 0.5));
        Assert.True(await store.CompleteAsync(currentClaim));

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.Completed, persisted.Status);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.Null(persisted.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task RetryAsync_ReleasesLeaseAndPreservesActiveDeduplication()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        await SeedMessageAsync(database, CreatePendingMessage(userId));

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);
        var claim = Assert.Single(await store.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));

        Assert.True(await store.RetryAsync(claim, jitterSample: 1));

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.Pending, persisted.Status);
        Assert.Equal(1, persisted.Attempts);
        Assert.True(persisted.NextAttemptAtUtc > DateTimeOffset.UtcNow);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.Equal($"email-confirmation:{userId:N}", persisted.ActiveDeduplicationKey);
        Assert.Empty(await store.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));
    }

    [SqlServerIntegrationFact]
    public async Task RetryAsync_DeadLettersAfterMaximumAttemptsAndRetainsDeduplication()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        await SeedMessageAsync(database, CreatePendingMessage(userId, attempts: 4));

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);
        var claim = Assert.Single(await store.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));

        Assert.Equal(EmailOutboxRetryPolicy.MaximumAttempts, claim.Attempts);
        Assert.True(await store.RetryAsync(claim, jitterSample: 0.5));

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, persisted.Status);
        Assert.Equal(EmailOutboxRetryPolicy.MaximumAttempts, persisted.Attempts);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.Equal($"email-confirmation:{userId:N}", persisted.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task DeadLetterAsync_RequiresCurrentLeaseAndRetainsDeduplication()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        await SeedMessageAsync(database, CreatePendingMessage(userId));

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);
        var claim = Assert.Single(await store.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));

        Assert.True(await store.DeadLetterAsync(claim));

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.DeadLetter, persisted.Status);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.Equal($"email-confirmation:{userId:N}", persisted.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task CancelAsync_RequiresCurrentRowVersionAndLeaseAndClearsDeduplication()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        await SeedMessageAsync(database, CreatePendingMessage(userId));

        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);
        var claim = Assert.Single(await store.ClaimDueAsync(1, TimeSpan.FromMinutes(2)));
        var staleRowVersion = claim.RowVersion.ToArray();
        staleRowVersion[0] ^= 0xff;
        var staleClaim = new EmailOutboxClaim(
            claim.Id,
            claim.UserId,
            claim.Attempts,
            claim.LeaseExpiresAtUtc,
            staleRowVersion);

        Assert.False(await store.CancelAsync(staleClaim));
        Assert.True(await store.CancelAsync(claim));

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.Cancelled, persisted.Status);
        Assert.Null(persisted.CompletedAtUtc);
        Assert.Null(persisted.LeaseExpiresAtUtc);
        Assert.Null(persisted.ActiveDeduplicationKey);
    }

    [SqlServerIntegrationFact]
    public async Task CancelAsync_RejectsExpiredLease()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = await CreateUserAsync(database);
        var message = new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            EmailOutboxMessageStatus.Processing,
            attempts: 1,
            createdAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            nextAttemptAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            activeDeduplicationKey: $"email-confirmation:{userId:N}",
            leaseExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));

        await SeedMessageAsync(database, message);

        var expiredClaim = new EmailOutboxClaim(
            message.Id,
            message.UserId,
            message.Attempts,
            message.LeaseExpiresAtUtc!.Value,
            message.RowVersion);
        await using var context = database.CreateVerificationContext();
        var store = new SqlServerEmailOutboxStore(context);

        Assert.False(await store.CancelAsync(expiredClaim));

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxMessageStatus.Processing, persisted.Status);
        Assert.NotNull(persisted.LeaseExpiresAtUtc);
        Assert.Equal($"email-confirmation:{userId:N}", persisted.ActiveDeduplicationKey);
    }

    private static async Task<Guid> CreateUserAsync(SqlServerTestDatabase database)
    {
        var email = $"outbox-{Guid.NewGuid():N}@example.test";
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email
        };
        var result = await database.Services
            .GetRequiredService<UserManager<XaquiamuniUser>>()
            .CreateAsync(user, "Outbox Integration 987!");

        Assert.True(result.Succeeded);
        return user.Id;
    }

    private static async Task SeedMessageAsync(
        SqlServerTestDatabase database,
        EmailOutboxMessage message)
    {
        await using var context = database.CreateVerificationContext();
        context.EmailOutboxMessages.Add(message);
        await context.SaveChangesAsync();
    }

    private static EmailOutboxMessage CreatePendingMessage(Guid userId, int attempts = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            EmailOutboxMessageStatus.Pending,
            attempts,
            now,
            now,
            activeDeduplicationKey: $"email-confirmation:{userId:N}");
    }
}
