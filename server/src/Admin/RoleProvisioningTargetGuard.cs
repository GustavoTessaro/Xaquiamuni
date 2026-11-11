using Microsoft.Data.SqlClient;

namespace Xaquiamuni.Admin;

public static class RoleProvisioningTargetGuard
{
    public const string ExpectedServer = @".\SQLEXPRESS";
    public const string ExpectedDatabase = "XaquiamuniDev";

    public const string InitialIdentityMigration = "20261111054514_InitialIdentitySchema";
    public const string ClienteProfileMigration = "20261111060917_AddClientePerfilEEnderecos";

    public static SqlConnectionStringBuilder ValidateConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RoleProvisioningPreconditionException(
                "A variável ConnectionStrings__SqlServer não está configurada.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(value);
        }
        catch (ArgumentException)
        {
            throw new RoleProvisioningPreconditionException("A connection string local é inválida.");
        }

        if (!string.Equals(builder.DataSource, ExpectedServer, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(builder.InitialCatalog, ExpectedDatabase, StringComparison.Ordinal)
            || !builder.IntegratedSecurity
            || !builder.Encrypt
            || !builder.TrustServerCertificate
            || !string.IsNullOrEmpty(builder.UserID)
            || !string.IsNullOrEmpty(builder.Password)
            || !string.IsNullOrEmpty(builder.AttachDBFilename)
            || builder.UserInstance)
        {
            throw new RoleProvisioningPreconditionException(
                "A connection string deve apontar exclusivamente para .\\SQLEXPRESS/XaquiamuniDev com autenticação integrada.");
        }

        return builder;
    }

    public static void ValidateConnectedTarget(
        string? instanceName,
        string? serverName,
        string? databaseName,
        string? localMachineName = null)
    {
        var machineName = localMachineName ?? Environment.MachineName;
        var serverParts = serverName?.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (!string.Equals(instanceName, "SQLEXPRESS", StringComparison.OrdinalIgnoreCase)
            || serverParts is not { Length: 2 }
            || !string.Equals(serverParts[0], machineName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(serverParts[1], "SQLEXPRESS", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(databaseName, ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new RoleProvisioningPreconditionException(
                "A conexão estabelecida não corresponde à instância local .\\SQLEXPRESS e ao banco XaquiamuniDev.");
        }
    }

    public static void ValidateAppliedMigrations(IEnumerable<string> migrations)
    {
        var applied = new HashSet<string>(migrations, StringComparer.Ordinal);
        if (!applied.Contains(InitialIdentityMigration) || !applied.Contains(ClienteProfileMigration))
        {
            throw new RoleProvisioningPreconditionException(
                "As migrations esperadas não estão aplicadas em XaquiamuniDev. Nenhuma migration será executada automaticamente.");
        }
    }
}

public sealed class RoleProvisioningPreconditionException : Exception
{
    public RoleProvisioningPreconditionException(string message)
        : base(message)
    {
    }
}
