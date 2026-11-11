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

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
}
