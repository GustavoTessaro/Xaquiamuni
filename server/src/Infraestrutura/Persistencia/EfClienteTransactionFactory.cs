using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfClienteTransactionFactory : IClienteTransactionFactory
{
    private readonly XaquiamuniDbContext _dbContext;

    public EfClienteTransactionFactory(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IClienteTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        return new EfClienteTransaction(_dbContext, transaction);
    }
}
