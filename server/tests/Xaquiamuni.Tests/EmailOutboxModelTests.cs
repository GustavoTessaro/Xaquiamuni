using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests;

public sealed class EmailOutboxModelTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Model_MapsOutboxColumnsAndSqlServerTypesWithoutSensitivePayloadFields()
    {
        using var context = CreateContext();
        var entity = GetEntity(context);
        var properties = entity.GetProperties().ToDictionary(property => property.Name);

        Assert.Equal("EmailOutbox", entity.GetTableName());
        Assert.Equal(
            new[]
            {
                nameof(EmailOutboxMessage.Id),
                nameof(EmailOutboxMessage.UserId),
                nameof(EmailOutboxMessage.MessageType),
                nameof(EmailOutboxMessage.Status),
                nameof(EmailOutboxMessage.Attempts),
                nameof(EmailOutboxMessage.CreatedAtUtc),
                nameof(EmailOutboxMessage.NextAttemptAtUtc),
                nameof(EmailOutboxMessage.CompletedAtUtc),
                nameof(EmailOutboxMessage.ActiveDeduplicationKey),
                nameof(EmailOutboxMessage.LeaseExpiresAtUtc),
                nameof(EmailOutboxMessage.RowVersion)
            }.OrderBy(name => name),
            properties.Keys.OrderBy(name => name));
        Assert.Equal("smallint", properties[nameof(EmailOutboxMessage.MessageType)].GetColumnType());
        Assert.Equal("smallint", properties[nameof(EmailOutboxMessage.Status)].GetColumnType());
        Assert.Equal("datetimeoffset(7)", properties[nameof(EmailOutboxMessage.CreatedAtUtc)].GetColumnType());
        Assert.Equal("datetimeoffset(7)", properties[nameof(EmailOutboxMessage.NextAttemptAtUtc)].GetColumnType());
        Assert.Equal("datetimeoffset(7)", properties[nameof(EmailOutboxMessage.CompletedAtUtc)].GetColumnType());
        Assert.Equal("datetimeoffset(7)", properties[nameof(EmailOutboxMessage.LeaseExpiresAtUtc)].GetColumnType());
        Assert.Equal("rowversion", properties[nameof(EmailOutboxMessage.RowVersion)].GetColumnType());
        Assert.True(properties[nameof(EmailOutboxMessage.CompletedAtUtc)].IsNullable);
        Assert.True(properties[nameof(EmailOutboxMessage.ActiveDeduplicationKey)].IsNullable);
        Assert.True(properties[nameof(EmailOutboxMessage.LeaseExpiresAtUtc)].IsNullable);
    }

    [Fact]
    public void Model_MapsPrimaryKeyCascadeForeignKeyAndUserIndex()
    {
        using var context = CreateContext();
        var entity = GetEntity(context);
        var primaryKey = Assert.Single(entity.GetKeys(), key => key.IsPrimaryKey());
        var foreignKey = Assert.Single(entity.GetForeignKeys());
        var userIndex = Assert.Single(entity.GetIndexes(), index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(EmailOutboxMessage.UserId));

        Assert.Equal(nameof(EmailOutboxMessage.Id), Assert.Single(primaryKey.Properties).Name);
        Assert.Equal(typeof(XaquiamuniUser), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(EmailOutboxMessage.UserId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal("IX_EmailOutbox_UserId", userIndex.GetDatabaseName());
    }

    [Fact]
    public void Model_MapsQueueIndexAndFilteredUniqueDeduplicationIndex()
    {
        using var context = CreateContext();
        var indexes = GetEntity(context).GetIndexes();
        var queueIndex = Assert.Single(indexes, index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(EmailOutboxMessage.Status),
                nameof(EmailOutboxMessage.NextAttemptAtUtc),
                nameof(EmailOutboxMessage.CreatedAtUtc)
            ]));
        var deduplicationIndex = Assert.Single(indexes, index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(EmailOutboxMessage.ActiveDeduplicationKey));

        Assert.Equal("IX_EmailOutbox_Queue", queueIndex.GetDatabaseName());
        Assert.False(queueIndex.IsUnique);
        Assert.Equal("UX_EmailOutbox_ActiveDeduplicationKey", deduplicationIndex.GetDatabaseName());
        Assert.True(deduplicationIndex.IsUnique);
        Assert.Equal("[ActiveDeduplicationKey] IS NOT NULL", deduplicationIndex.GetFilter());
    }

    [Fact]
    public void Model_MapsRowVersionAsGeneratedConcurrencyToken()
    {
        using var context = CreateContext();
        var property = GetEntity(context).FindProperty(nameof(EmailOutboxMessage.RowVersion))!;

        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void Model_MapsCheckConstraintsForAttemptsAndRestrictedEnumValues()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailOutboxMessage))!;
        var constraints = entity.GetCheckConstraints()
            .ToDictionary(constraint => constraint.Name!, constraint => constraint.Sql!);

        Assert.Equal("[Attempts] >= 0", constraints["CK_EmailOutbox_Attempts_NonNegative"]);
        Assert.Equal("[MessageType] IN (1)", constraints["CK_EmailOutbox_MessageType_Valid"]);
        Assert.Equal("[Status] IN (1, 2, 3, 4, 5)", constraints["CK_EmailOutbox_Status_Valid"]);
        Assert.Equal(
            "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [NextAttemptAtUtc]) = 0 AND "
            + "([CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET, [CompletedAtUtc]) = 0) AND "
            + "([LeaseExpiresAtUtc] IS NULL OR DATEPART(TZOFFSET, [LeaseExpiresAtUtc]) = 0)",
            constraints["CK_EmailOutbox_Timestamps_Utc"]);
    }

    [Fact]
    public void Constructor_RequiresUtcTimestampsAndNonNegativeAttempts()
    {
        Assert.Throws<ArgumentException>(() => CreateMessage(createdAtUtc: UtcNow.ToOffset(TimeSpan.FromHours(-3))));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMessage(attempts: -1));
    }

    [Fact]
    public void Constructor_RejectsEnumValuesNotRepresentedByTheModel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateMessage(messageType: (EmailOutboxMessageType)2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateMessage(status: (EmailOutboxMessageStatus)99));
    }

    private static XaquiamuniDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniOutboxModelTest")
            .Options;
        return new XaquiamuniDbContext(options);
    }

    private static IEntityType GetEntity(XaquiamuniDbContext context) =>
        context.Model.FindEntityType(typeof(EmailOutboxMessage))!;

    private static EmailOutboxMessage CreateMessage(
        int attempts = 0,
        DateTimeOffset? createdAtUtc = null,
        EmailOutboxMessageType messageType = EmailOutboxMessageType.EmailConfirmation,
        EmailOutboxMessageStatus status = EmailOutboxMessageStatus.Pending) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            messageType,
            status,
            attempts,
            createdAtUtc ?? UtcNow,
            UtcNow);
}
