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
