using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xaquiamuni.Admin;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class AdminAccountProvisioningCompositionSqlServerIntegrationTests
{
    private const string TestPassword = "Admin Test Password 987!";

    [SqlServerIntegrationFact]
    public async Task AdminCliComposition_ProvisionsAdministratorWithRegisteredIdentityServices()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var verificationContext = database.CreateVerificationContext();
        var connectionString = verificationContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = connectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddAdminProvisioningServices(configuration);

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = serviceProvider.CreateAsyncScope();
        var scopedServices = scope.ServiceProvider;
        var roleInitializer = scopedServices.GetRequiredService<IdentityRoleInitializer>();
        await roleInitializer.EnsureRolesAsync();

        var provisioner = scopedServices.GetRequiredService<IAdminAccountProvisioner>();
        var userManager = scopedServices.GetRequiredService<UserManager<XaquiamuniUser>>();
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var result = await provisioner.ProvisionAsync(email, TestPassword);

        Assert.Equal(AdminAccountProvisioningStatus.Created, result);
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await userManager.IsInRoleAsync(user, XaquiamuniRoles.Administrador));
    }

    [SqlServerIntegrationFact]
    public async Task AdminProvisioning_RollsBackCreatedUserIfRoleAssignmentFails()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var verificationContext = database.CreateVerificationContext();
        var connectionString = verificationContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = connectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddAdminProvisioningServices(configuration);

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = serviceProvider.CreateAsyncScope();
        var scopedServices = scope.ServiceProvider;
        await scopedServices.GetRequiredService<IdentityRoleInitializer>().EnsureRolesAsync();
        var userManager = scopedServices.GetRequiredService<UserManager<XaquiamuniUser>>();
        var provisioner = scopedServices.GetRequiredService<IAdminAccountProvisioner>();
        await verificationContext.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [dbo].[TR_Test_RejectAdminRoleAssignment]
            ON [dbo].[AspNetUserRoles]
            AFTER INSERT
            AS
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM inserted AS added
                    INNER JOIN [dbo].[AspNetRoles] AS roles ON roles.[Id] = added.[RoleId]
                    WHERE roles.[NormalizedName] = N'ADMIN'
                )
                BEGIN
                    THROW 51000, 'Injected test failure during ADMIN role assignment.', 1;
                END
            END;
            """);

        var email = $"admin-{Guid.NewGuid():N}@example.test";
        await Assert.ThrowsAsync<DbUpdateException>(
            () => provisioner.ProvisionAsync(email, TestPassword));

        Assert.Null(await userManager.FindByEmailAsync(email));
    }
}
