using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests;

public sealed class EmailOutboxStoreContractTests
{
    [Fact]
    public void Claim_ValidatesAndDefensivelyCopiesRowVersion()
    {
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var claim = CreateClaim(rowVersion);
        rowVersion[0] = 9;

        Assert.Equal(1, claim.RowVersion.Span[0]);
        Assert.Equal("[EmailOutboxClaim]", claim.ToString());
        Assert.Throws<ArgumentException>(() => CreateClaim([1, 2, 3]));
        Assert.Throws<ArgumentException>(
            () => new EmailOutboxClaim(
                Guid.NewGuid(),
                Guid.NewGuid(),
                attempts: 1,
                DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)),
                new byte[8]));
    }

    [Fact]
    public void DependencyInjection_RegistersScopedSqlServerOutboxStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniOutboxStoreTest"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructureServices(configuration);

        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IEmailOutboxStore));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>();
        var context = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
        var storeContext = store.GetType()
            .GetField("_dbContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store);

        Assert.Same(context, storeContext);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task ClaimDueAsync_RejectsInvalidOptionsWithoutOpeningDatabaseConnection()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniOutboxStoreValidationTest")
            .Options;
        using var context = new XaquiamuniDbContext(options);
        var store = new SqlServerEmailOutboxStore(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.ClaimDueAsync(batchSize: 0, TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.ClaimDueAsync(batchSize: 1, TimeSpan.Zero));

        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static EmailOutboxClaim CreateClaim(byte[] rowVersion) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            attempts: 1,
            DateTimeOffset.UtcNow,
            rowVersion);
}
