using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfClienteTransaction : IClienteTransaction
{
    private readonly XaquiamuniDbContext _dbContext;
    private readonly IDbContextTransaction _transaction;
    private bool _completed;
    private bool _disposed;

    public EfClienteTransaction(
        XaquiamuniDbContext dbContext,
        IDbContextTransaction transaction)
    {
        _dbContext = dbContext;
        _transaction = transaction;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsDuplicateCpf(exception))
        {
            throw new ClienteCpfDuplicadoException();
        }
        catch (DbUpdateException)
        {
            throw new ClientePersistenceException();
        }
        catch (DbException)
        {
            throw new ClientePersistenceException();
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        try
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException)
        {
            throw new ClientePersistenceException();
        }

        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_completed)
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
                _completed = true;
            }
        }
        finally
        {
            _disposed = true;
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("A transação já foi concluída.");
        }
    }

    private static bool IsDuplicateCpf(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(error =>
                    (error.Number is 2601 or 2627)
                    && error.Message.Contains("UX_ClientePerfis_CpfFingerprint", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
