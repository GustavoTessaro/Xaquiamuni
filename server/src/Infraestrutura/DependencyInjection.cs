using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Aplicacao.Autenticacao;
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
        services.AddScoped<IClienteLoginIdentityService, IdentityClienteLoginService>();
        services.AddScoped<IEmailConfirmationService, IdentityEmailConfirmationService>();
        services.AddScoped<ILoginCliente, LoginCliente>();
        services.AddScoped<CadastroClienteInputValidator>();
        services.AddScoped<ICadastrarCliente, CadastrarCliente>();

        var identityBuilder = services
            .AddIdentityCore<XaquiamuniUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<XaquiamuniDbContext>()
            .AddSignInManager();

        identityBuilder
            .AddDefaultTokenProviders()
            .AddTokenProvider<XaquiamuniEmailConfirmationTokenProvider>(
                XaquiamuniEmailConfirmationTokenProvider.ProviderName);
        services.Configure<IdentityOptions>(options =>
        {
            options.Tokens.EmailConfirmationTokenProvider =
                XaquiamuniEmailConfirmationTokenProvider.ProviderName;
        });
        services.Configure<XaquiamuniEmailConfirmationTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(24);
        });

        services.Replace(ServiceDescriptor.Scoped<SignInManager<XaquiamuniUser>>(serviceProvider =>
        {
            var signInManager = ActivatorUtilities.CreateInstance<SignInManager<XaquiamuniUser>>(serviceProvider);
            signInManager.AuthenticationScheme = XaquiamuniIdentityDefaults.AuthenticationScheme;
            return signInManager;
        }));

        services.Configure<CpfProtectionOptions>(configuration.GetSection("CpfProtection"));
        services.AddSingleton<ICpfProtectionService, AesGcmCpfProtectionService>();

        return services;
    }
}
