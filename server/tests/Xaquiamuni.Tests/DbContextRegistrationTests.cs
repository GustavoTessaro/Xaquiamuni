using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public class DbContextRegistrationTests
{
    [Fact]
    public void AddInfrastructureServices_RegistersSqlServerProviderWithoutConnecting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniTest"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", dbContext.Database.ProviderName);
    }

    [Fact]
    public void AddInfrastructureServices_RegistersIdentityManagersAndIdentityModel()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniIdentityModelTest"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        Assert.NotNull(scopedServices.GetRequiredService<UserManager<XaquiamuniUser>>());
        Assert.NotNull(scopedServices.GetRequiredService<RoleManager<IdentityRole<Guid>>>());

        var dbContext = scopedServices.GetRequiredService<XaquiamuniDbContext>();
        Assert.Contains(dbContext.Model.GetEntityTypes(), entityType =>
            entityType.ClrType == typeof(IdentityUserRole<Guid>));
        Assert.Contains(dbContext.Model.GetEntityTypes(), entityType =>
            entityType.ClrType == typeof(IdentityRole<Guid>));
    }

    [Fact]
    public void AddInfrastructureServices_RegistersScopedRepositorySharingIdentityDbContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniScopedRepositoryTest"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IClienteRepository));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var repository = scopedServices.GetRequiredService<IClienteRepository>();
        var dbContext = scopedServices.GetRequiredService<XaquiamuniDbContext>();
        var userManager = scopedServices.GetRequiredService<UserManager<XaquiamuniUser>>();
        var userStore = scopedServices.GetRequiredService<IUserStore<XaquiamuniUser>>();
        var contextProperty = userStore.GetType().GetProperty("Context");

        Assert.NotNull(repository);
        Assert.NotNull(userManager);
        Assert.NotNull(contextProperty);
        Assert.Same(dbContext, contextProperty!.GetValue(userStore));
    }

    [Fact]
    public void AddInfrastructureServices_RegistersScopedTransactionFactorySharingContextWithoutStartingTransaction()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniTransactionFactoryTest"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IClienteTransactionFactory));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var transactionFactory = scopedServices.GetRequiredService<IClienteTransactionFactory>();
        var dbContext = scopedServices.GetRequiredService<XaquiamuniDbContext>();
        var userStore = scopedServices.GetRequiredService<IUserStore<XaquiamuniUser>>();
        var storeContext = userStore.GetType().GetProperty("Context")!.GetValue(userStore);
        var transactionContext = transactionFactory.GetType()
            .GetField("_dbContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(transactionFactory);

        Assert.Same(dbContext, storeContext);
        Assert.Same(dbContext, transactionContext);
        Assert.Equal(System.Data.ConnectionState.Closed, dbContext.Database.GetDbConnection().State);
        Assert.Null(dbContext.Database.CurrentTransaction);
    }

    [Fact]
    public void AddInfrastructureServices_RegistersCustomerUseCaseAndIdentityAdapterAsScoped()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniRegistrationTest",
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
        services.AddInfrastructureServices(configuration);

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ICadastrarCliente)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IClienteIdentityService)).Lifetime);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scoped = scope.ServiceProvider;
        var useCase = scoped.GetRequiredService<ICadastrarCliente>();
        var identityAdapter = scoped.GetRequiredService<IClienteIdentityService>();
        var transactionFactory = scoped.GetRequiredService<IClienteTransactionFactory>();
        var dbContext = scoped.GetRequiredService<XaquiamuniDbContext>();
        var identityManager = scoped.GetRequiredService<UserManager<XaquiamuniUser>>();
        var userStore = scoped.GetRequiredService<IUserStore<XaquiamuniUser>>();
        var userStoreContext = userStore.GetType().GetProperty("Context")!.GetValue(userStore);
        var transactionContext = transactionFactory.GetType()
            .GetField("_dbContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(transactionFactory);

        Assert.NotNull(useCase);
        Assert.NotNull(identityAdapter);
        Assert.Same(dbContext, userStoreContext);
        Assert.Same(dbContext, transactionContext);
        Assert.Equal(System.Data.ConnectionState.Closed, dbContext.Database.GetDbConnection().State);
        Assert.Null(dbContext.Database.CurrentTransaction);
        Assert.NotNull(identityManager);
    }

    [Fact]
    public void AddInfrastructureServices_RegistersRoleInitializerExplicitlyWithoutExecutingIt()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=XaquiamuniRoleInitializerRegistrationTest"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IdentityRoleInitializer));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IdentityRoleInitializer>();
        var context = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();

        Assert.NotNull(initializer);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public void IdentityModel_EnforcesUniqueNormalizedEmailWhenPresent()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniIdentityModelTest")
            .Options;
        using var dbContext = new XaquiamuniDbContext(options);

        var userEntity = dbContext.Model.FindEntityType(typeof(XaquiamuniUser));
        var emailIndex = Assert.Single(userEntity!.GetIndexes(), index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(XaquiamuniUser.NormalizedEmail));

        Assert.True(emailIndex.IsUnique);
        Assert.Equal("[NormalizedEmail] IS NOT NULL", emailIndex.GetFilter());
        Assert.Equal("EmailIndex", emailIndex.GetDatabaseName());
    }
}
