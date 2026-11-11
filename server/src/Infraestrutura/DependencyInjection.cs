using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Seguranca;

namespace Xaquiamuni.Infraestrutura;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SqlServer");

        services.AddDbContext<XaquiamuniDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        });
        services.AddScoped<IClienteRepository, ClienteRepository>();
        services.AddScoped<IClienteTransactionFactory, EfClienteTransactionFactory>();
        services.AddScoped<IClienteIdentityService, IdentityClienteIdentityService>();
        services.AddScoped<IRoleManagerOperations, IdentityRoleManagerOperations>();
        services.AddScoped<IdentityRoleInitializer>();
        services.AddScoped<CadastroClienteInputValidator>();
        services.AddScoped<ICadastrarCliente, CadastrarCliente>();

        services
            .AddIdentityCore<XaquiamuniUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<XaquiamuniDbContext>();

        services.Configure<CpfProtectionOptions>(configuration.GetSection("CpfProtection"));
        services.AddSingleton<ICpfProtectionService, AesGcmCpfProtectionService>();

        return services;
    }
}
