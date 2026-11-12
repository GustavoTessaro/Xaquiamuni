using System.Data;
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
public sealed class PasswordResetCredentialGenerationMigrationSqlServerIntegrationTests
{
    private const string PreviousMigration = "20261112023420_AddPasswordResetEmailOutboxType";
    private const string FingerprintMigration =
        "20261112033043_AddPasswordResetCredentialGenerationFingerprint";

    [SqlServerIntegrationFact]
    public async Task Up_AddsFingerprintAndRequiresItOnlyForPasswordResetMessages()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: PreviousMigration);
        var userId = await CreateUserAsync(database);
        var confirmationId = Guid.NewGuid();
        await InsertLegacyMessageAsync(
            database,
            confirmationId,
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            activeDeduplicationKey: $"email-confirmation:{userId:N}");
        var rowVersionBefore = await ReadRowVersionAsync(database, confirmationId);
        var indexesBefore = await ReadCatalogValuesAsync(database, """
            SELECT [name]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'dbo.EmailOutbox') AND [index_id] > 0
            ORDER BY [name];
            """);
        var relationshipsBefore = await ReadCatalogValuesAsync(database, """
            SELECT CONCAT([name], N'->', OBJECT_SCHEMA_NAME([referenced_object_id]),
                          N'.', OBJECT_NAME([referenced_object_id]))
            FROM sys.foreign_keys
            WHERE [parent_object_id] = OBJECT_ID(N'dbo.EmailOutbox')
            ORDER BY [name];
            """);

        await MigrateToAsync(database, FingerprintMigration);

        await using (var context = database.CreateVerificationContext())
        {
            Assert.False(context.Database.HasPendingModelChanges());
            var confirmation = await context.EmailOutboxMessages
                .AsNoTracking()
                .SingleAsync(message => message.Id == confirmationId);
            Assert.Null(confirmation.CredentialGenerationFingerprint);

            var validPasswordReset = new EmailOutboxMessage(
                Guid.NewGuid(),
                userId,
                EmailOutboxMessageType.PasswordReset,
                EmailOutboxMessageStatus.Completed,
                attempts: 1,
                createdAtUtc: DateTimeOffset.UtcNow,
                nextAttemptAtUtc: DateTimeOffset.UtcNow,
                completedAtUtc: DateTimeOffset.UtcNow,
                credentialGenerationFingerprint: new byte[32]);
            context.EmailOutboxMessages.Add(validPasswordReset);
            await context.SaveChangesAsync();
        }

        Assert.Equal("binary|32|1", await ReadFingerprintColumnAsync(database));
        Assert.Equal(rowVersionBefore, await ReadRowVersionAsync(database, confirmationId));
        Assert.Equal(indexesBefore, await ReadCatalogValuesAsync(database, """
            SELECT [name]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'dbo.EmailOutbox') AND [index_id] > 0
            ORDER BY [name];
            """));
        Assert.Equal(relationshipsBefore, await ReadCatalogValuesAsync(database, """
            SELECT CONCAT([name], N'->', OBJECT_SCHEMA_NAME([referenced_object_id]),
                          N'.', OBJECT_NAME([referenced_object_id]))
            FROM sys.foreign_keys
            WHERE [parent_object_id] = OBJECT_ID(N'dbo.EmailOutbox')
            ORDER BY [name];
            """));

        await Assert.ThrowsAsync<SqlException>(
            () => InsertRawMessageAsync(
                database,
                Guid.NewGuid(),
                userId,
                EmailOutboxMessageType.PasswordReset,
                credentialGenerationFingerprint: null));
        await Assert.ThrowsAsync<SqlException>(
            () => InsertRawMessageAsync(
                database,
                Guid.NewGuid(),
                userId,
                EmailOutboxMessageType.EmailConfirmation,
                credentialGenerationFingerprint: new byte[32]));
    }

    [SqlServerIntegrationFact]
    public async Task Down_RejectsPasswordResetRowsWithoutLosingTheirFingerprint()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: PreviousMigration);
        var userId = await CreateUserAsync(database);
        await MigrateToAsync(database, FingerprintMigration);
        var messageId = Guid.NewGuid();
        var fingerprint = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        await InsertRawMessageAsync(
            database,
            messageId,
            userId,
            EmailOutboxMessageType.PasswordReset,
            fingerprint);

        await Assert.ThrowsAsync<SqlException>(
            () => MigrateToAsync(database, PreviousMigration));

        await using var context = database.CreateVerificationContext();
        Assert.Contains(FingerprintMigration, await context.Database.GetAppliedMigrationsAsync());
        var persisted = await context.EmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.Id == messageId);
        Assert.Equal(fingerprint, persisted.CredentialGenerationFingerprint);
    }

    [SqlServerIntegrationFact]
    public async Task Down_WithoutPasswordResetRowsPreservesConfirmationRows()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: PreviousMigration);
        var userId = await CreateUserAsync(database);
        var messageId = Guid.NewGuid();
        await InsertLegacyMessageAsync(
            database,
            messageId,
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            activeDeduplicationKey: $"email-confirmation:{userId:N}");
        var rowVersionBefore = await ReadRowVersionAsync(database, messageId);
        await MigrateToAsync(database, FingerprintMigration);

        await MigrateToAsync(database, PreviousMigration);

        Assert.DoesNotContain(
            FingerprintMigration,
            await GetAppliedMigrationsAsync(database));
        Assert.Equal(
            (short)EmailOutboxMessageType.EmailConfirmation,
            await ReadMessageTypeAsync(database, messageId));
        Assert.Equal(rowVersionBefore, await ReadRowVersionAsync(database, messageId));
    }

    [SqlServerIntegrationFact]
    public async Task Up_WithHistoricalPasswordResetRowsWithoutFingerprintRollsBackSafely()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: PreviousMigration);
        var userId = await CreateUserAsync(database);
        var messageId = Guid.NewGuid();
        await InsertLegacyMessageAsync(
            database,
            messageId,
            userId,
            EmailOutboxMessageType.PasswordReset,
            activeDeduplicationKey: $"password-reset:{userId:N}");
        var rowVersionBefore = await ReadRowVersionAsync(database, messageId);

        await Assert.ThrowsAsync<SqlException>(
            () => MigrateToAsync(database, FingerprintMigration));

        Assert.DoesNotContain(FingerprintMigration, await GetAppliedMigrationsAsync(database));
        Assert.Null(await ReadFingerprintColumnAsync(database));
        Assert.Equal(rowVersionBefore, await ReadRowVersionAsync(database, messageId));
        Assert.Equal(
            (short)EmailOutboxMessageType.PasswordReset,
            await ReadMessageTypeAsync(database, messageId));
        Assert.Empty(await ReadCatalogValuesAsync(database, """
            SELECT [name]
            FROM sys.check_constraints
            WHERE [parent_object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                AND [name] = N'CK_EmailOutbox_CredentialGenerationFingerprint_Valid';
            """));
    }

    private static async Task<Guid> CreateUserAsync(SqlServerTestDatabase database)
    {
        var email = $"credential-generation-{Guid.NewGuid():N}@example.test";
        var user = new XaquiamuniUser
        {
            UserName = email,
            Email = email
        };
        var result = await database.Services
            .GetRequiredService<UserManager<XaquiamuniUser>>()
            .CreateAsync(user, "Credential Generation 987!");
        Assert.True(result.Succeeded);
        return user.Id;
    }

    private static async Task InsertLegacyMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId,
        Guid userId,
        EmailOutboxMessageType messageType,
        string? activeDeduplicationKey)
    {
        await using var context = database.CreateVerificationContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[EmailOutbox]
                ([Id], [UserId], [MessageType], [Status], [Attempts],
                 [CreatedAtUtc], [NextAttemptAtUtc], [CompletedAtUtc],
                 [ActiveDeduplicationKey], [LeaseExpiresAtUtc])
            VALUES
                ({messageId}, {userId}, {(short)messageType},
                 {(short)EmailOutboxMessageStatus.Pending}, 0,
                 {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow}, NULL,
                 {activeDeduplicationKey}, NULL);
            """);
    }

    private static async Task InsertRawMessageAsync(
        SqlServerTestDatabase database,
        Guid messageId,
        Guid userId,
        EmailOutboxMessageType messageType,
        byte[]? credentialGenerationFingerprint)
    {
        await using var context = database.CreateVerificationContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO [dbo].[EmailOutbox]
                    ([Id], [UserId], [MessageType], [Status], [Attempts],
                     [CreatedAtUtc], [NextAttemptAtUtc], [CompletedAtUtc],
                     [ActiveDeduplicationKey], [LeaseExpiresAtUtc],
                     [CredentialGenerationFingerprint])
                VALUES
                    (@Id, @UserId, @MessageType, @Status, 0,
                     @NowUtc, @NowUtc, @NowUtc, NULL, NULL,
                     @CredentialGenerationFingerprint);
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.UniqueIdentifier) { Value = userId });
            command.Parameters.Add(new SqlParameter("@MessageType", SqlDbType.SmallInt) { Value = (short)messageType });
            command.Parameters.Add(new SqlParameter("@Status", SqlDbType.SmallInt)
            {
                Value = (short)EmailOutboxMessageStatus.Completed
            });
            command.Parameters.Add(new SqlParameter("@NowUtc", SqlDbType.DateTimeOffset)
            {
                Value = DateTimeOffset.UtcNow
            });
            command.Parameters.Add(new SqlParameter("@CredentialGenerationFingerprint", SqlDbType.Binary, 32)
            {
                Value = credentialGenerationFingerprint is null
                    ? DBNull.Value
                    : credentialGenerationFingerprint
            });
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<short> ReadMessageTypeAsync(
        SqlServerTestDatabase database,
        Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [MessageType] FROM [dbo].[EmailOutbox] WHERE [Id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
        return (short)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<byte[]> ReadRowVersionAsync(
        SqlServerTestDatabase database,
        Guid messageId)
    {
        await using var context = database.CreateVerificationContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT [RowVersion] FROM [dbo].[EmailOutbox] WHERE [Id] = @Id;";
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
            return (byte[])(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<string?> ReadFingerprintColumnAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CONCAT([type].[name], N'|', [column].[max_length], N'|', [column].[is_nullable])
                FROM sys.columns AS [column]
                JOIN sys.types AS [type] ON [column].[user_type_id] = [type].[user_type_id]
                WHERE [column].[object_id] = OBJECT_ID(N'dbo.EmailOutbox')
                    AND [column].[name] = N'CredentialGenerationFingerprint';
                """;
            return (string?)await command.ExecuteScalarAsync();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<string[]> ReadCatalogValuesAsync(
        SqlServerTestDatabase database,
        string sql)
    {
        await using var context = database.CreateVerificationContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
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

    private static async Task MigrateToAsync(
        SqlServerTestDatabase database,
        string targetMigration)
    {
        await using var context = database.CreateVerificationContext();
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static async Task<string[]> GetAppliedMigrationsAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateVerificationContext();
        return (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    }
}
