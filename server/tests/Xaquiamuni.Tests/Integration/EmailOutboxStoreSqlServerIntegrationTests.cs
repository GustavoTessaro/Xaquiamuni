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
            firstStore.ClaimDueAsync(batchSize: 1, TimeSpan.FromMinutes(2)),
            secondStore.ClaimDueAsync(batchSize: 1, TimeSpan.FromMinutes(2)));

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
