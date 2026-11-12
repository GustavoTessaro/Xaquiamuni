using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class EmailOutboxTypeMigrationSqlServerIntegrationTests
{
    private const string AddEmailOutboxMigration = "20261111221631_AddEmailOutbox";
    private const string AddPasswordResetEmailOutboxTypeMigration =
        "20261112023420_AddPasswordResetEmailOutboxType";

    [SqlServerIntegrationFact]
    public async Task Up_AcceptsOnlyKnownTypesAndPreservesExistingDataAndSchema()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: AddEmailOutboxMigration);
        var userId = await CreateUserAsync(database);
        var confirmation = CreateMessage(
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            $"email-confirmation:{userId:N}");
        await SeedMessageAsync(database, confirmation);

        var before = await ReadMessageAsync(database, confirmation.Id);
        var indexesBefore = await ReadCatalogValuesAsync(
            database,
            """
            SELECT [name]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                AND [index_id] > 0
            ORDER BY [name];
            """);
        var relationshipsBefore = await ReadCatalogValuesAsync(
            database,
            """
            SELECT CONCAT([name], N'->', OBJECT_SCHEMA_NAME([referenced_object_id]),
                          N'.', OBJECT_NAME([referenced_object_id]))
            FROM sys.foreign_keys
            WHERE [parent_object_id] = OBJECT_ID(N'dbo.EmailOutbox')
            ORDER BY [name];
            """);

        await MigrateToLatestAsync(database);

        await using (var context = database.CreateVerificationContext())
        {
            Assert.False(context.Database.HasPendingModelChanges());
        }

        Assert.Contains(
            AddPasswordResetEmailOutboxTypeMigration,
            await GetAppliedMigrationsAsync(database));

        var after = await ReadMessageAsync(database, confirmation.Id);
        AssertMessageUnchanged(before, after);
        Assert.Equal(
            indexesBefore,
            await ReadCatalogValuesAsync(
                database,
                """
                SELECT [name]
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                    AND [index_id] > 0
                ORDER BY [name];
                """));
        Assert.Equal(
            relationshipsBefore,
            await ReadCatalogValuesAsync(
                database,
                """
                SELECT CONCAT([name], N'->', OBJECT_SCHEMA_NAME([referenced_object_id]),
                              N'.', OBJECT_NAME([referenced_object_id]))
                FROM sys.foreign_keys
                WHERE [parent_object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                ORDER BY [name];
                """));

        var passwordReset = CreateMessage(
            userId,
            EmailOutboxMessageType.PasswordReset,
            $"password-reset:{userId:N}");
        await SeedMessageAsync(database, passwordReset);
        await Assert.ThrowsAsync<SqlException>(
            () => InsertUnknownMessageTypeAsync(database, userId, messageType: 3));

        var exception = await Record.ExceptionAsync(
            () => MigrateToAsync(database, AddEmailOutboxMigration));
        Assert.IsType<SqlException>(exception);

        var messagesAfterRejectedDown = await ReadMessagesAsync(database);
        Assert.Equal(2, messagesAfterRejectedDown.Count);
        AssertMessageUnchanged(
            before,
            Assert.Single(messagesAfterRejectedDown, message => message.Id == confirmation.Id));
        Assert.Equal(
            EmailOutboxMessageType.PasswordReset,
            Assert.Single(messagesAfterRejectedDown, message => message.Id == passwordReset.Id).MessageType);
        Assert.Contains(
            AddPasswordResetEmailOutboxTypeMigration,
            await GetAppliedMigrationsAsync(database));
        await Assert.ThrowsAsync<SqlException>(
            () => InsertUnknownMessageTypeAsync(database, userId, messageType: 3));
    }

    [SqlServerIntegrationFact]
    public async Task Down_WithoutPasswordResetMessagesPreservesConfirmationRows()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: AddEmailOutboxMigration);
        var userId = await CreateUserAsync(database);
        var confirmation = CreateMessage(
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            $"email-confirmation:{userId:N}");
        await SeedMessageAsync(database, confirmation);
        var before = await ReadMessageAsync(database, confirmation.Id);
        var indexesBefore = await ReadCatalogValuesAsync(
            database,
            """
            SELECT [name]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                AND [index_id] > 0
            ORDER BY [name];
            """);
        var relationshipsBefore = await ReadCatalogValuesAsync(
            database,
            """
            SELECT CONCAT([name], N'->', OBJECT_SCHEMA_NAME([referenced_object_id]),
                          N'.', OBJECT_NAME([referenced_object_id]))
            FROM sys.foreign_keys
            WHERE [parent_object_id] = OBJECT_ID(N'dbo.EmailOutbox')
            ORDER BY [name];
            """);

        await MigrateToLatestAsync(database);
        await MigrateToAsync(database, AddEmailOutboxMigration);

        var after = await ReadMessageAsync(database, confirmation.Id);
        AssertMessageUnchanged(before, after);
        Assert.DoesNotContain(
            AddPasswordResetEmailOutboxTypeMigration,
            await GetAppliedMigrationsAsync(database));
        Assert.Equal(
            indexesBefore,
            await ReadCatalogValuesAsync(
                database,
                """
                SELECT [name]
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                    AND [index_id] > 0
                ORDER BY [name];
                """));
        Assert.Equal(
            relationshipsBefore,
            await ReadCatalogValuesAsync(
                database,
                """
                SELECT CONCAT([name], N'->', OBJECT_SCHEMA_NAME([referenced_object_id]),
                              N'.', OBJECT_NAME([referenced_object_id]))
                FROM sys.foreign_keys
                WHERE [parent_object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                ORDER BY [name];
                """));
        await Assert.ThrowsAsync<SqlException>(
            () => InsertUnknownMessageTypeAsync(database, userId, messageType: 2));
    }

    private static async Task<Guid> CreateUserAsync(SqlServerTestDatabase database)
    {
        var email = $"outbox-migration-{Guid.NewGuid():N}@example.test";
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email
        };
        var result = await database.Services
            .GetRequiredService<UserManager<XaquiamuniUser>>()
            .CreateAsync(user, "Migration Test 987!");

        Assert.True(result.Succeeded);
        return user.Id;
    }

    private static EmailOutboxMessage CreateMessage(
        Guid userId,
        EmailOutboxMessageType messageType,
        string activeDeduplicationKey)
    {
        var now = DateTimeOffset.UtcNow;
        return new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            messageType,
            EmailOutboxMessageStatus.Pending,
            attempts: 0,
            createdAtUtc: now,
            nextAttemptAtUtc: now,
            activeDeduplicationKey: activeDeduplicationKey);
    }

    private static async Task SeedMessageAsync(
        SqlServerTestDatabase database,
        EmailOutboxMessage message)
    {
        await using var context = database.CreateVerificationContext();
        context.EmailOutboxMessages.Add(message);
        await context.SaveChangesAsync();
    }

    private static async Task<EmailOutboxMessage> ReadMessageAsync(
        SqlServerTestDatabase database,
        Guid id)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.Id == id);
    }

    private static async Task<List<EmailOutboxMessage>> ReadMessagesAsync(
        SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        return await context.EmailOutboxMessages.AsNoTracking().ToListAsync();
    }

    private static async Task InsertUnknownMessageTypeAsync(
        SqlServerTestDatabase database,
        Guid userId,
        short messageType)
    {
        await using var context = database.CreateVerificationContext();
        var now = DateTimeOffset.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[EmailOutbox]
                ([Id], [UserId], [MessageType], [Status], [Attempts],
                 [CreatedAtUtc], [NextAttemptAtUtc], [CompletedAtUtc],
                 [ActiveDeduplicationKey], [LeaseExpiresAtUtc])
            VALUES
                ({Guid.NewGuid()}, {userId}, {messageType},
                 {(short)EmailOutboxMessageStatus.Completed}, 1,
                 {now}, {now}, {now}, NULL, NULL);
            """);
    }

    private static async Task<string[]> ReadCatalogValuesAsync(
        SqlServerTestDatabase database,
        string sql)
    {
        await using var context = database.CreateVerificationContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            var values = new List<string>();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return values.ToArray();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task MigrateToLatestAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.MigrateAsync();
    }

    private static async Task MigrateToAsync(
        SqlServerTestDatabase database,
        string targetMigration)
    {
        await using var context = database.CreateVerificationContext();
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static async Task<string[]> GetAppliedMigrationsAsync(
        SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        return (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    }

    private static void AssertMessageUnchanged(
        EmailOutboxMessage expected,
        EmailOutboxMessage actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.UserId, actual.UserId);
        Assert.Equal(expected.MessageType, actual.MessageType);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Attempts, actual.Attempts);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.NextAttemptAtUtc, actual.NextAttemptAtUtc);
        Assert.Equal(expected.CompletedAtUtc, actual.CompletedAtUtc);
        Assert.Equal(expected.ActiveDeduplicationKey, actual.ActiveDeduplicationKey);
        Assert.Equal(expected.LeaseExpiresAtUtc, actual.LeaseExpiresAtUtc);
        Assert.Equal(expected.RowVersion, actual.RowVersion);
    }
}
