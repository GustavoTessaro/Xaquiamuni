using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Admin;

public interface IRoleProvisioningPreflight
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlServerRoleProvisioningPreflight : IRoleProvisioningPreflight
{
    private readonly XaquiamuniDbContext _dbContext;

    public SqlServerRoleProvisioningPreflight(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CONVERT(nvarchar(128), SERVERPROPERTY('InstanceName')),
                       CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')),
                       DB_NAME();
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new RoleProvisioningPreconditionException(
                    "Não foi possível validar o destino SQL Server.");
            }

            RoleProvisioningTargetGuard.ValidateConnectedTarget(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
            await reader.DisposeAsync().ConfigureAwait(false);

            var appliedMigrations = await _dbContext.Database
                .GetAppliedMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);
            RoleProvisioningTargetGuard.ValidateAppliedMigrations(appliedMigrations);
        }
        catch (DbException)
        {
            throw new RoleProvisioningPreconditionException(
                "Não foi possível verificar o destino e o histórico de migrations do banco local autorizado.");
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
