using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfProdutoRepository : IProdutoRepository
{
    private const string VariantIdentityIndex =
        "UX_ProdutoVariantes_ProdutoId_IdentificacaoNormalizada";
    private const string DefaultVariantIndex =
        "UX_ProdutoVariantes_DefaultPerProduto";
    private const string VariantCombinationIndex =
        "UX_ProdutoVariantes_ProdutoId_CombinacaoHash";
    private static readonly string[] ProductCatalogUniqueConstraints =
    [
        "UX_ProdutoCaracteristicas_ProdutoId_NomeNormalizado",
        "UX_ProdutoCaracteristicas_ProdutoId_TipoMedidaFisica",
        "UX_ProdutoCaracteristicaValores_Produto_Caracteristica_NomeNormalizado",
        "PK_ProdutoVarianteCaracteristicaValores"
    ];
    private static readonly string[] ProductCatalogForeignKeys =
    [
        "FK_ProdutoCaracteristicaValores_ProdutoCaracteristicas",
        "FK_ProdutoVarianteCaracteristicaValores_ProdutoCaracteristicaValores",
        "FK_ProdutoVarianteCaracteristicaValores_ProdutoCaracteristicas",
        "FK_ProdutoVarianteCaracteristicaValores_ProdutoVariantes"
    ];

    private readonly XaquiamuniDbContext _dbContext;

    public EfProdutoRepository(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Produto>> ListarAsync(
        ProdutoFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var produtos = _dbContext.Produtos
            .AsNoTracking()
            .Include(produto => produto.Categoria)
            .Include(produto => produto.Variantes)
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Caracteristica)
            .Include(produto => produto.Variantes)
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Valor)
            .Include(produto => produto.Caracteristicas)
                .ThenInclude(caracteristica => caracteristica.Valores)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            produtos = produtos.Where(produto => produto.Nome.Contains(busca));
        }

        if (filtro.CategoriaId is not null)
        {
            produtos = produtos.Where(produto => produto.CategoriaId == filtro.CategoriaId.Value);
        }

        if (filtro.Publicado is not null)
        {
            produtos = produtos.Where(produto => produto.Publicado == filtro.Publicado.Value);
        }

        return await produtos
            .OrderBy(produto => produto.Nome)
            .ThenBy(produto => produto.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Produto?> ConsultarAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        IncluirAgregado(_dbContext.Produtos)
            .AsNoTracking()
            .SingleOrDefaultAsync(produto => produto.Id == id, cancellationToken);

    public async Task<ProdutoResult> ExecutarMutacaoAsync(
        Func<IProdutoMutationSession, CancellationToken, Task<ProdutoResult>> operacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        var committed = false;

        try
        {
            var session = new ProdutoMutationSession(_dbContext);
            var result = await operacao(session, cancellationToken).ConfigureAwait(false);
            if (result.Status != ProdutoStatus.Success)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }

            await SaveChangesWithDefaultSwitchAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            committed = true;
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PersistenceConflict();
        }
        catch (DbUpdateException exception) when (IsPersistenceConflict(exception))
        {
            return PersistenceConflict();
        }
        catch (SqlException exception) when (IsPersistenceConflict(exception))
        {
            return PersistenceConflict();
        }
        finally
        {
            if (!committed)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task SaveChangesWithDefaultSwitchAsync(CancellationToken cancellationToken)
    {
        var demotedDefaults = _dbContext.ChangeTracker
            .Entries<ProdutoVariante>()
            .Where(entry => entry.State == EntityState.Modified
                && entry.Property(variante => variante.Padrao).OriginalValue
                && !entry.Entity.Padrao)
            .ToArray();

        foreach (var entry in demotedDefaults)
        {
            var rowsUpdated = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [ProdutoVariantes] SET [Padrao] = CAST(0 AS bit) WHERE [Id] = {entry.Entity.Id} AND [ProdutoId] = {entry.Entity.ProdutoId} AND [Padrao] = CAST(1 AS bit)",
                    cancellationToken)
                .ConfigureAwait(false);
            if (rowsUpdated != 1)
            {
                throw new DbUpdateConcurrencyException(
                    "The stored default variant changed during the product update.");
            }

            var defaultProperty = entry.Property(variante => variante.Padrao);
            defaultProperty.OriginalValue = false;
            defaultProperty.IsModified = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProdutoResult PersistenceConflict() =>
        ProdutoResult.Conflict(
            "O produto foi alterado por outra operação ou os dados conflitam com uma restrição existente.");

    private static bool IsPersistenceConflict(Exception exception)
    {
        for (Exception? innerException = exception;
             innerException is not null;
             innerException = innerException.InnerException)
        {
            if (innerException is SqlException { Number: 1205 or 1222 })
            {
                return true;
            }

            if (innerException is not SqlException sqlException)
            {
                continue;
            }

            if (sqlException.Number is 2601 or 2627
                && (sqlException.Message.Contains(VariantIdentityIndex, StringComparison.OrdinalIgnoreCase)
                    || sqlException.Message.Contains(DefaultVariantIndex, StringComparison.OrdinalIgnoreCase)
                    || sqlException.Message.Contains(VariantCombinationIndex, StringComparison.OrdinalIgnoreCase)
                    || ProductCatalogUniqueConstraints.Any(constraint =>
                        sqlException.Message.Contains(constraint, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            if (sqlException.Number == 547
                && (sqlException.Message.Contains(
                        "FK_Produtos_Categorias_CategoriaId",
                        StringComparison.OrdinalIgnoreCase)
                    || ProductCatalogForeignKeys.Any(constraint =>
                        sqlException.Message.Contains(constraint, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ProdutoMutationSession(XaquiamuniDbContext dbContext) : IProdutoMutationSession
    {
        public Task<Categoria?> BuscarCategoriaAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            dbContext.Categorias.SingleOrDefaultAsync(
                categoria => categoria.Id == id,
                cancellationToken);

        public Task<Produto?> BuscarProdutoAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            IncluirAgregado(dbContext.Produtos)
                .SingleOrDefaultAsync(produto => produto.Id == id, cancellationToken);

        public void Adicionar(Produto produto)
        {
            ArgumentNullException.ThrowIfNull(produto);
            dbContext.Produtos.Add(produto);
        }
    }

    private static IQueryable<Produto> IncluirAgregado(IQueryable<Produto> produtos) =>
        produtos
            .Include(produto => produto.Categoria)
            .Include(produto => produto.Caracteristicas)
                .ThenInclude(caracteristica => caracteristica.Valores)
            .Include(produto => produto.Variantes)
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Caracteristica)
            .Include(produto => produto.Variantes)
                .ThenInclude(variante => variante.CaracteristicaValores)
                    .ThenInclude(associacao => associacao.Valor)
            .AsSplitQuery();
}
