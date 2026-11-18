using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Catalogo.Imagens;
using Xaquiamuni.Aplicacao.Catalogo.Publico;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Imagens;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Pedidos;
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
        services.AddOptions<CalendarioOperacionalEstoqueOptions>()
            .Bind(configuration.GetSection(CalendarioOperacionalEstoque.SectionName))
            .Validate(
                options => CalendarioOperacionalEstoque.ZonaValida(options.TimeZoneId),
                "The stock operational time zone must be a valid time zone identifier.")
            .ValidateOnStart();
        services.AddSingleton<ICalendarioOperacionalEstoque, CalendarioOperacionalEstoque>();
        services.AddScoped<IClienteRepository, ClienteRepository>();
        services.AddScoped<ICategoriaRepository, EfCategoriaRepository>();
        services.AddScoped<IGerenciarCategorias, GerenciarCategorias>();
        services.AddScoped<IProdutoRepository, EfProdutoRepository>();
        services.AddScoped<IGerenciarProdutos, GerenciarProdutos>();
        services.AddScoped<ICatalogoPublicoRepository, EfCatalogoPublicoRepository>();
        services.AddScoped<IConsultarCatalogoPublico, ConsultarCatalogoPublico>();
        services.AddScoped<IGerenciarImagensProduto, GerenciarImagensProduto>();
        services.AddScoped<IEstoqueInsumoRepository, EfEstoqueInsumoRepository>();
        services.AddScoped<IAdministrarInsumoEstoque, AdministrarInsumoEstoque>();
        services.AddScoped<IEstoqueInsumoConsultaRepository, EfEstoqueInsumoConsultaRepository>();
        services.AddScoped<IConsultarEstoqueInsumo, ConsultarEstoqueInsumo>();
        services.AddScoped<IRegistrarEntradaInsumo, RegistrarEntradaInsumo>();
        services.AddScoped<ICriarPedidoCompraRepository, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<CriarPedidoCompra>();
        services.AddScoped<IConfirmarPagamentoPedidoRepository, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<ConfirmarPagamentoPedido>();
        services.AddScoped<IPrepararPedidoCompraRepository, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<PrepararPedidoCompra>();
        services.AddScoped<IEncerrarPedidoCompraRepository, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<EncerrarPedidoCompra>();
        services.AddScoped<IConsultarPedidosCliente, EfConsultaPedidosCliente>();
        services.AddScoped<IConsultarPedidosAdmin, EfConsultaPedidosAdmin>();
        services.AddScoped<IEstoqueProdutoAcabadoRepository, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<IReservaProdutoAcabadoRepository, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<IReservarProdutoAcabado, ReservarProdutoAcabado>();
        services.AddScoped<ICicloReservasProdutoAcabado, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<IConsumirReservasProdutoAcabado, EfEstoqueProdutoAcabadoRepository>();
        services.AddScoped<IRegistrarEntradaProdutoAcabado, RegistrarEntradaProdutoAcabado>();
        services.AddScoped<IRegistrarOperacaoProdutoAcabado, RegistrarOperacaoProdutoAcabado>();
        services.AddScoped<IContarProdutoAcabado, ContarProdutoAcabado>();
        services.AddScoped<IEstornarMovimentoProdutoAcabado, EstornarMovimentoProdutoAcabado>();
        services.AddScoped<IConfigurarPoliticaEstoqueProdutoAcabado, ConfigurarPoliticaEstoqueProdutoAcabado>();
        services.AddScoped<IEstoqueProdutoAcabadoConsultaRepository, EfEstoqueProdutoAcabadoConsultaRepository>();
        services.AddScoped<IConsultarEstoqueProdutoAcabado, ConsultarEstoqueProdutoAcabado>();
        services.AddScoped<IRegistrarSaidaInsumo, RegistrarSaidaInsumo>();
        services.AddScoped<IAjustarContagemInsumo, AjustarContagemInsumo>();
        services.AddScoped<IEstornarMovimentoInsumo, EstornarMovimentoInsumo>();
        services.AddScoped<IAtualizarPoliticaLoteInsumo, AtualizarPoliticaLoteInsumo>();
        services.AddOptions<ArmazenamentoLocalImagemProdutoOptions>()
            .Bind(configuration.GetSection(ArmazenamentoLocalImagemProdutoOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RootPath)
                    && Path.IsPathFullyQualified(options.RootPath),
                "Product image storage RootPath must be an absolute path.")
            .ValidateOnStart();
        services.AddSingleton<IArmazenamentoImagemProduto, ArmazenamentoLocalImagemProduto>();
        services.AddSingleton<IProcessadorImagemProduto, ProcessadorImagemProduto>();
        services.AddSingleton<IProdutoImagemOperacaoLogger, ProdutoImagemOperacaoLogger>();
        services.AddScoped<IClienteTransactionFactory, EfClienteTransactionFactory>();
        services.AddScoped<IEmailConfirmationOutbox, EmailConfirmationOutbox>();
        services.AddScoped<IEmailConfirmationResendOutbox, SqlServerEmailConfirmationResendOutbox>();
        services.AddScoped<IEmailOutboxStore, SqlServerEmailOutboxStore>();
        services.AddScoped<IClienteIdentityService, IdentityClienteIdentityService>();
        services.AddScoped<IRoleManagerOperations, IdentityRoleManagerOperations>();
        services.AddScoped<IdentityRoleInitializer>();
        services.AddScoped<IAdministradorLoginIdentityService, IdentityAdministradorLoginService>();
        services.AddScoped<IClienteLoginIdentityService, IdentityClienteLoginService>();
        services.AddScoped<IEmailConfirmationService, IdentityEmailConfirmationService>();
        services.AddScoped<IPasswordResetTransactionCoordinator, SqlServerPasswordResetTransactionCoordinator>();
        services.AddScoped<
            IPasswordResetOutboxGenerationCancellation,
            SqlServerPasswordResetOutboxGenerationCancellation>();
        services.AddScoped<IPasswordResetService>(serviceProvider =>
            new IdentityPasswordResetService(
                serviceProvider.GetRequiredService<UserManager<XaquiamuniUser>>(),
                serviceProvider.GetRequiredService<ICredentialGenerationFingerprint>(),
                serviceProvider.GetRequiredService<IPasswordResetTransactionCoordinator>(),
                serviceProvider.GetRequiredService<IPasswordResetOutboxGenerationCancellation>()));
        services.AddScoped<IPasswordResetOutbox, SqlServerPasswordResetOutbox>();
        services.AddScoped<ISolicitarRecuperacaoSenha, SolicitarRecuperacaoSenha>();
        services.AddScoped<ISolicitarReenvioConfirmacaoEmail, SolicitarReenvioConfirmacaoEmail>();
        services.AddSingleton<IEmailConfirmationMessageComposer, EmailConfirmationMessageComposer>();
        services.AddSingleton<IPasswordResetMessageComposer, PasswordResetMessageComposer>();
        services.AddScoped<ILoginCliente, LoginCliente>();
        services.AddScoped<ILoginAdministrador, LoginAdministrador>();
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
                XaquiamuniEmailConfirmationTokenProvider.ProviderName)
            .AddTokenProvider<XaquiamuniPasswordResetTokenProvider>(
                XaquiamuniPasswordResetTokenProvider.ProviderName);
        services.Configure<IdentityOptions>(options =>
        {
            options.Tokens.EmailConfirmationTokenProvider =
                XaquiamuniEmailConfirmationTokenProvider.ProviderName;
            options.Tokens.PasswordResetTokenProvider =
                XaquiamuniPasswordResetTokenProvider.ProviderName;
        });
        services.Configure<XaquiamuniEmailConfirmationTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(24);
        });
        services.Configure<XaquiamuniPasswordResetTokenProviderOptions>(options =>
        {
            options.Name = XaquiamuniPasswordResetTokenProvider.ProtectionPurpose;
            options.TokenLifespan = TimeSpan.FromHours(1);
        });

        services.AddScoped<XaquiamuniSignInManager>(serviceProvider =>
        {
            var signInManager = ActivatorUtilities.CreateInstance<XaquiamuniSignInManager>(serviceProvider);
            signInManager.AuthenticationScheme = XaquiamuniIdentityDefaults.AuthenticationScheme;
            return signInManager;
        });
        services.Replace(ServiceDescriptor.Scoped<SignInManager<XaquiamuniUser>>(
            serviceProvider => serviceProvider.GetRequiredService<XaquiamuniSignInManager>()));

        services.Configure<CpfProtectionOptions>(configuration.GetSection("CpfProtection"));
        services.AddSingleton<ICpfProtectionService, AesGcmCpfProtectionService>();
        services.AddSingleton<ICredentialGenerationFingerprint, Sha256CredentialGenerationFingerprint>();
        services.Configure<EmailConfirmationEmailOptions>(
            configuration.GetSection(EmailConfirmationEmailOptions.SectionName));

        var resendEmailOptions = configuration
            .GetSection(ResendEmailOptions.SectionName)
            .Get<ResendEmailOptions>() ?? new ResendEmailOptions();
        if (resendEmailOptions.Enabled)
        {
            services.AddResendEmailSender(configuration);
        }

        var expiracaoWorkerSection = configuration.GetSection(ExpiracaoPedidosWorkerOptions.SectionName);
        services.AddOptions<ExpiracaoPedidosWorkerOptions>()
            .Bind(expiracaoWorkerSection)
            .Validate(options => options.CycleInterval >= TimeSpan.FromSeconds(5)
                && options.CycleInterval <= TimeSpan.FromHours(1), "Invalid order expiration interval.")
            .Validate(options => options.BatchSize is >= 1 and <= 100, "Invalid order expiration batch size.")
            .Validate(options => options.MaxPagesPerCycle is >= 1 and <= 20, "Invalid order expiration page limit.")
            .ValidateOnStart();
        if (expiracaoWorkerSection.GetValue<bool>(nameof(ExpiracaoPedidosWorkerOptions.Enabled)))
        {
            services.AddHostedService<ExpiracaoPedidosBackgroundService>();
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

            services.AddScoped<EmailConfirmationOutboxProcessor>();
            services.AddScoped<IEmailOutboxProcessor>(serviceProvider =>
                serviceProvider.GetRequiredService<EmailConfirmationOutboxProcessor>());
            services.AddScoped<PasswordResetOutboxProcessor>();
            services.AddScoped<IEmailOutboxProcessor>(serviceProvider =>
                serviceProvider.GetRequiredService<PasswordResetOutboxProcessor>());
            services.AddScoped<EmailOutboxDispatcher>();
            services.AddHostedService<EmailOutboxBackgroundService>();
        }

        return services;
    }
}
