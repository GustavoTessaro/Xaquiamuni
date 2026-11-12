using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests;

public sealed class EmailConfirmationOutboxTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EnqueueEmailConfirmation_AddsPendingMessageWithUtcDefaultsWithoutPersisting()
    {
        using var context = CreateContext();
        var service = new EmailConfirmationOutbox(context, new FixedTimeProvider(UtcNow));
        var userId = Guid.NewGuid();

        service.EnqueueEmailConfirmation(userId);

        var entry = Assert.Single(context.ChangeTracker.Entries<EmailOutboxMessage>());
        var message = entry.Entity;
        Assert.Equal(EntityState.Added, entry.State);
        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(userId, message.UserId);
        Assert.Equal(EmailOutboxMessageType.EmailConfirmation, message.MessageType);
        Assert.Equal(EmailOutboxMessageStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.Equal(UtcNow, message.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, message.CreatedAtUtc.Offset);
        Assert.Equal(UtcNow, message.NextAttemptAtUtc);
        Assert.Null(message.CompletedAtUtc);
        Assert.Equal($"email-confirmation:{userId:N}", message.ActiveDeduplicationKey);
        Assert.Null(message.LeaseExpiresAtUtc);
        Assert.Empty(message.RowVersion);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public void EnqueueEmailConfirmation_DeduplicatesLocallyAndUsesStableKeyAcrossContexts()
    {
        var userId = Guid.NewGuid();
        using var firstContext = CreateContext();
        var firstService = new EmailConfirmationOutbox(firstContext, new FixedTimeProvider(UtcNow));

        firstService.EnqueueEmailConfirmation(userId);
        firstService.EnqueueEmailConfirmation(userId);

        var firstMessage = Assert.Single(firstContext.EmailOutboxMessages.Local);

        using var secondContext = CreateContext();
        var secondService = new EmailConfirmationOutbox(secondContext, new FixedTimeProvider(UtcNow));
        secondService.EnqueueEmailConfirmation(userId);
        var secondMessage = Assert.Single(secondContext.EmailOutboxMessages.Local);

        Assert.Equal(firstMessage.ActiveDeduplicationKey, secondMessage.ActiveDeduplicationKey);
        Assert.Equal(System.Data.ConnectionState.Closed, firstContext.Database.GetDbConnection().State);
        Assert.Equal(System.Data.ConnectionState.Closed, secondContext.Database.GetDbConnection().State);
    }

    [Fact]
    public void DependencyInjection_RegistersScopedOutboxUsingTheScopedContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniOutboxServiceTest",
                ["CpfProtection:CurrentEncryptionKeyVersion"] = "enc-test",
                ["CpfProtection:EncryptionKeys:enc-test"] = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                ["CpfProtection:CurrentFingerprintKeyVersion"] = "fp-test",
                ["CpfProtection:FingerprintKeys:fp-test"] = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddInfrastructureServices(configuration);

        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IEmailConfirmationOutbox));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var outbox = scoped.GetRequiredService<IEmailConfirmationOutbox>();
        var context = scoped.GetRequiredService<XaquiamuniDbContext>();
        var useCase = scoped.GetRequiredService<ICadastrarCliente>();
        var transactionFactory = scoped.GetRequiredService<IClienteTransactionFactory>();
        var useCaseOutbox = useCase.GetType()
            .GetField("_emailConfirmationOutbox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(useCase);
        var outboxContext = outbox.GetType()
            .GetField("_dbContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(outbox);
        var transactionContext = transactionFactory.GetType()
            .GetField("_dbContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(transactionFactory);

        outbox.EnqueueEmailConfirmation(Guid.NewGuid());

        Assert.Same(outbox, useCaseOutbox);
        Assert.Same(context, outboxContext);
        Assert.Same(context, transactionContext);
        Assert.Single(context.EmailOutboxMessages.Local);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static XaquiamuniDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniOutboxUnitTest")
            .Options;
        return new XaquiamuniDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
