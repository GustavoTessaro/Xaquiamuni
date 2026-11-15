using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfCategoriaRepository : ICategoriaRepository
{
    private const string NomeIndexName = "UX_Categorias_NomeNormalizado";

    private readonly XaquiamuniDbContext _dbContext;

    public EfCategoriaRepository(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Categoria>> ListarAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.Categorias
            .AsNoTracking()
            .OrderBy(categoria => categoria.OrdemExibicao)
            .ThenBy(categoria => categoria.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<Categoria?> BuscarPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _dbContext.Categorias
            .SingleOrDefaultAsync(categoria => categoria.Id == id, cancellationToken);

    public Task<bool> ExisteNomeNormalizadoAsync(
        string nomeNormalizado,
        Guid? excetoId = null,
        CancellationToken cancellationToken = default)
    {
        var categorias = _dbContext.Categorias
            .Where(categoria => categoria.NomeNormalizado == nomeNormalizado);
        if (excetoId is not null)
        {
            categorias = categorias.Where(categoria => categoria.Id != excetoId.Value);
        }

        return categorias.AnyAsync(cancellationToken);
    }

    public async Task<bool> AdicionarAsync(
        Categoria categoria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categoria);
        _dbContext.Categorias.Add(categoria);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (IsCategoryNameConflict(exception))
        {
            _dbContext.Entry(categoria).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> AtualizarAsync(
        Categoria categoria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categoria);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!categoria.Ativa)
            {
                await _dbContext.Produtos
                    .Where(produto => produto.CategoriaId == categoria.Id && produto.Publicado)
                    .ExecuteUpdateAsync(
                        updates => updates.SetProperty(produto => produto.Publicado, false),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (IsCategoryNameConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.Entry(categoria).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsCategoryNameConflict(DbUpdateException exception)
    {
        for (Exception? innerException = exception; innerException is not null; innerException = innerException.InnerException)
        {
            if (innerException is SqlException { Number: 2601 or 2627 } sqlException
                && sqlException.Message.Contains(NomeIndexName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
