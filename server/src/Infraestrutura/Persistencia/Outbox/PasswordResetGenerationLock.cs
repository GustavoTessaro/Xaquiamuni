using System.Data;
using Microsoft.Data.SqlClient;

namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

internal static class PasswordResetGenerationLock
{
    private const int ApplicationLockTimeoutMilliseconds = 5000;

    public static string GetResourceName(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        return $"PasswordResetGeneration:{userId:N}";
    }

    public static async Task AcquireAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @LockTimeoutMilliseconds,
                @DbPrincipal = 'public';
            SELECT @LockResult;
            """;

        var resource = command.Parameters.Add("@Resource", SqlDbType.NVarChar, 255);
        resource.Value = GetResourceName(userId);
        var timeout = command.Parameters.Add("@LockTimeoutMilliseconds", SqlDbType.Int);
        timeout.Value = ApplicationLockTimeoutMilliseconds;

        var lockResult = (int?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (lockResult is null)
        {
            throw new InvalidOperationException("The password reset lock returned no result.");
        }

        if (lockResult.Value < 0)
        {
            if (lockResult.Value == -2 && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new InvalidOperationException(
                $"The password reset lock could not be acquired (SQL result {lockResult.Value}).");
        }
    }
}
