namespace Xaquiamuni.Tests.Integration;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerIntegrationFactAttribute : FactAttribute
{
    public SqlServerIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("XAQUIAMUNI_RUN_SQLSERVER_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set XAQUIAMUNI_RUN_SQLSERVER_INTEGRATION=1 to create isolated local SQL Server test databases.";
        }
    }
}
