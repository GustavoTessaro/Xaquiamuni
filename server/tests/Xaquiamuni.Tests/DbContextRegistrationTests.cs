using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
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
}
