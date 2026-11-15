using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xaquiamuni.Admin;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Admin;

internal static class Program
{
    private const string ProvisionAdminArgument = "--provision-admin";
    private const string ProvisionAdminConfirmation = "CRIAR ADMIN";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 1
            || (args.Length == 1
                && !string.Equals(args[0], ProvisionAdminArgument, StringComparison.Ordinal)))
        {
            Console.Error.WriteLine($"Uso: Xaquiamuni.Admin [{ProvisionAdminArgument}]");
            return 2;
        }

        var provisionAdministrator = args.Length == 1;
        var rawConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer");
        try
        {
            var connectionString = RoleProvisioningTargetGuard.ValidateConnectionString(rawConnectionString);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString.ConnectionString
                })
                .Build();

            Console.WriteLine(@"Destino autorizado: .\SQLEXPRESS / XaquiamuniDev");
            Console.WriteLine($"Papéis a verificar: {string.Join(", ", RoleProvisioningWorkflow.RolesToEnsure)}");
            Console.WriteLine("Nenhuma migration será executada. Nenhum papel existente será alterado.");
            if (provisionAdministrator)
            {
                Console.WriteLine("Modo: provisionar uma conta ADMIN local, sem cadastro público.");
            }
            Console.Write($"Para continuar, digite exatamente '{RoleProvisioningWorkflow.ConfirmationText}': ");
            var confirmation = Console.ReadLine();

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddAdminProvisioningServices(configuration);

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();
            var preflight = scope.ServiceProvider.GetRequiredService<IRoleProvisioningPreflight>();
            var workflow = new RoleProvisioningWorkflow(
                preflight,
                async cancellationToken =>
                {
                    var initializer = scope.ServiceProvider.GetRequiredService<IdentityRoleInitializer>();
                    await initializer.EnsureRolesAsync(cancellationToken).ConfigureAwait(false);
                });

            var status = await workflow.ExecuteAsync(confirmation);
            if (status == RoleProvisioningCommandStatus.NotConfirmed)
            {
                Console.WriteLine("Confirmação não recebida. Nenhuma verificação ou escrita foi realizada.");
                return 1;
            }

            if (!provisionAdministrator)
            {
                Console.WriteLine("Papéis Identity verificados; somente os ausentes foram criados.");
                return 0;
            }

            return await ProvisionAdministratorAsync(scope.ServiceProvider).ConfigureAwait(false);
        }
        catch (RoleProvisioningPreconditionException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (IdentityRoleInitializationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Falha inesperada no provisionamento local.");
            return 1;
        }
    }

    private static async Task<int> ProvisionAdministratorAsync(IServiceProvider services)
    {
        Console.Write($"Para criar uma conta ADMIN, digite exatamente '{ProvisionAdminConfirmation}': ");
        if (!string.Equals(Console.ReadLine(), ProvisionAdminConfirmation, StringComparison.Ordinal))
        {
            Console.WriteLine("Confirmação não recebida. Nenhuma conta foi criada.");
            return 1;
        }

        Console.Write("E-mail da proprietária: ");
        var email = Console.ReadLine();
        var password = ReadSecret("Senha (entrada oculta): ");
        var passwordConfirmation = ReadSecret("Confirme a senha (entrada oculta): ");
        if (!string.Equals(password, passwordConfirmation, StringComparison.Ordinal))
        {
            Console.WriteLine("As senhas não correspondem. Nenhuma conta foi criada.");
            return 1;
        }

        var provisioner = services.GetRequiredService<IAdminAccountProvisioner>();
        var status = await provisioner.ProvisionAsync(email, password).ConfigureAwait(false);
        if (status == AdminAccountProvisioningStatus.Created)
        {
            Console.WriteLine("Conta ADMIN provisionada. Nenhuma credencial foi exibida.");
            return 0;
        }

        Console.WriteLine(status switch
        {
            AdminAccountProvisioningStatus.InvalidInput =>
                "Os dados informados são inválidos. Nenhuma conta foi criada.",
            AdminAccountProvisioningStatus.IdentityRejected =>
                "O Identity rejeitou a criação da conta. Verifique a política de senha e se o e-mail já está cadastrado.",
            AdminAccountProvisioningStatus.RoleAssignmentFailed =>
                "Não foi possível atribuir o papel ADMIN. Nenhuma conta foi criada.",
            _ => throw new ArgumentOutOfRangeException(nameof(status), "Unsupported admin provisioning status.")
        });
        return 1;
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var secret = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return secret.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
