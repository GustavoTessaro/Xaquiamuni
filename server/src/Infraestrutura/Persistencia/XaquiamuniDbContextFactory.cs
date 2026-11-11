using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class XaquiamuniDbContextFactory : IDesignTimeDbContextFactory<XaquiamuniDbContext>
{
    public XaquiamuniDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__SqlServer to a SQL Server connection string for design-time EF operations.");
        }

        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new XaquiamuniDbContext(options);
    }
}
