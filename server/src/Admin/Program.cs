using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xaquiamuni.Admin;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Admin;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Este comando não aceita argumentos.");
            return 2;
        }

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
            Console.Write($"Para continuar, digite exatamente '{RoleProvisioningWorkflow.ConfirmationText}': ");
            var confirmation = Console.ReadLine();

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddInfrastructureServices(configuration);
            services.AddScoped<IRoleProvisioningPreflight, SqlServerRoleProvisioningPreflight>();

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

            Console.WriteLine("Papéis Identity verificados; somente os ausentes foram criados.");
            return 0;
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
            Console.Error.WriteLine("Falha inesperada no provisionamento local de papéis.");
            return 1;
        }
    }
}
