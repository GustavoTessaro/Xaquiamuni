using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Infraestrutura;

namespace Xaquiamuni.Admin;

public static class AdminProvisioningServiceCollectionExtensions
{
    public static IServiceCollection AddAdminProvisioningServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddInfrastructureServices(configuration);
        services.AddScoped<IRoleProvisioningPreflight, SqlServerRoleProvisioningPreflight>();
        services.AddScoped<IAdminAccountProvisioner, AdminAccountProvisioner>();
        return services;
    }
}
