using Microsoft.Extensions.Configuration;

namespace Xaquiamuni.PreVoo;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("[BLOQUEIO] Argumentos — Ação: informe servidor SQL esperado, banco esperado e diretório da API.");
            return 2;
        }

        var aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var ambiente = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? aspnet;
        if (ambiente is not ("Staging" or "Homologacao") || (aspnet is not null && aspnet != ambiente))
        {
            Console.WriteLine("[BLOQUEIO] Ambiente — Ação: declare DOTNET_ENVIRONMENT=Staging ou Homologacao e alinhe ASPNETCORE_ENVIRONMENT, se definido.");
            return 1;
        }

        try
        {
            // Reproduz a configuração da API fora de Development, sem criar host ou serviços.
            using var config = new ConfigurationManager();
            config.SetBasePath(Path.GetFullPath(args[2]));
            config.AddEnvironmentVariables("ASPNETCORE_");
            config.AddEnvironmentVariables("DOTNET_");
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            config.AddJsonFile($"appsettings.{ambiente}.json", optional: true, reloadOnChange: false);
            config.AddEnvironmentVariables();
            return DiagnosticoHomologacao.Executar(config, ambiente, args[0], args[1], Console.Out);
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException or FormatException
            or ArgumentException or InvalidOperationException)
        {
            Console.WriteLine("[BLOQUEIO] Configuração — Falha de leitura local. Ação: revise a sintaxe dos arquivos e as permissões de leitura.");
            return 1;
        }
    }
}
