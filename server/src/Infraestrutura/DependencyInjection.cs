using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;
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
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IClienteRepository, ClienteRepository>();
        services.AddScoped<IClienteTransactionFactory, EfClienteTransactionFactory>();
        services.AddScoped<IEmailConfirmationOutbox, EmailConfirmationOutbox>();
        services.AddScoped<IEmailConfirmationResendOutbox, SqlServerEmailConfirmationResendOutbox>();
        services.AddScoped<IEmailOutboxStore, SqlServerEmailOutboxStore>();
        services.AddScoped<EmailConfirmationOutboxProcessor>();
        services.AddScoped<IClienteIdentityService, IdentityClienteIdentityService>();
        services.AddScoped<IRoleManagerOperations, IdentityRoleManagerOperations>();
        services.AddScoped<IdentityRoleInitializer>();
        services.AddScoped<IClienteLoginIdentityService, IdentityClienteLoginService>();
        services.AddScoped<IEmailConfirmationService, IdentityEmailConfirmationService>();
        services.AddScoped<ISolicitarReenvioConfirmacaoEmail, SolicitarReenvioConfirmacaoEmail>();
        services.AddSingleton<IEmailConfirmationMessageComposer, EmailConfirmationMessageComposer>();
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
        services.Configure<EmailConfirmationEmailOptions>(
            configuration.GetSection(EmailConfirmationEmailOptions.SectionName));

        var resendEmailOptions = configuration
            .GetSection(ResendEmailOptions.SectionName)
            .Get<ResendEmailOptions>() ?? new ResendEmailOptions();
        if (resendEmailOptions.Enabled)
        {
            services.AddResendEmailSender(configuration);
        }

        var outboxWorkerSection = configuration.GetSection(EmailOutboxBackgroundServiceOptions.SectionName);
        services.AddOptions<EmailOutboxBackgroundServiceOptions>()
            .Bind(outboxWorkerSection)
            .Validate(
                options => options.CycleInterval >= EmailOutboxBackgroundServiceOptions.MinimumCycleInterval
                    && options.CycleInterval <= EmailOutboxBackgroundServiceOptions.MaximumCycleInterval,
                "Email outbox worker cycle interval is outside the supported range.")
            .Validate(
                options => options.BatchSize is >= EmailOutboxBackgroundServiceOptions.MinimumBatchSize
                    and <= EmailOutboxBackgroundServiceOptions.MaximumBatchSize,
                "Email outbox worker batch size is outside the supported range.")
            .ValidateOnStart();

        var outboxWorkerOptions = outboxWorkerSection.Get<EmailOutboxBackgroundServiceOptions>()
            ?? new EmailOutboxBackgroundServiceOptions();
        if (outboxWorkerOptions.Enabled)
        {
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(IEmailSender)))
            {
                throw new InvalidOperationException(
                    "The email outbox worker requires a configured email sender.");
            }

            services.AddHostedService<EmailOutboxBackgroundService>();
        }

        return services;
    }
}
