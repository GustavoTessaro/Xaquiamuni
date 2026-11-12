using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Infraestrutura.Identidade;

internal interface IPasswordResetTransactionCoordinator
{
    Task<Guid?> FindUserIdByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<IPasswordResetTransaction> BeginAsync(
        Guid userId,
        CancellationToken cancellationToken);
}

internal interface IPasswordResetTransaction : IAsyncDisposable
{
    Task<bool> ReloadUserAsync(XaquiamuniUser user, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

internal sealed class SqlServerPasswordResetTransactionCoordinator(
    XaquiamuniDbContext dbContext) : IPasswordResetTransactionCoordinator
{
    private readonly XaquiamuniDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public Task<Guid?> FindUserIdByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .Where(user => user.NormalizedEmail == normalizedEmail)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IPasswordResetTransaction> BeginAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Password reset requires its own short SQL transaction.");
        }

        if (_dbContext.Database.GetDbConnection() is not SqlConnection connection)
        {
            throw new InvalidOperationException(
                "Password reset coordination requires the SQL Server provider.");
        }

        var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (transaction.GetDbTransaction() is not SqlTransaction sqlTransaction)
            {
                throw new InvalidOperationException(
                    "Password reset coordination requires a SQL Server transaction.");
            }

            await PasswordResetGenerationLock
                .AcquireAsync(connection, sqlTransaction, userId, cancellationToken)
                .ConfigureAwait(false);

            return new SqlServerPasswordResetTransaction(_dbContext, transaction);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private sealed class SqlServerPasswordResetTransaction(
        XaquiamuniDbContext dbContext,
        IDbContextTransaction transaction) : IPasswordResetTransaction
    {
        private bool _completed;

        public async Task<bool> ReloadUserAsync(
            XaquiamuniUser user,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(user);
            await dbContext.Entry(user).ReloadAsync(cancellationToken).ConfigureAwait(false);
            return dbContext.Entry(user).State != EntityState.Detached;
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The password reset transaction is already complete.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            if (_completed)
            {
                return;
            }

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_completed)
                {
                    await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
