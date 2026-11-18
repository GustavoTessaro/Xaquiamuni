using System.Data;
using System.Runtime.ExceptionServices;
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
    private const string ImageMainIndex =
        "UX_ProdutoImagens_PrincipalPorProduto";
    private static readonly string[] ProductCatalogUniqueConstraints =
    [
        "UX_ProdutoCaracteristicas_ProdutoId_NomeNormalizado",
        "UX_ProdutoCaracteristicas_ProdutoId_TipoMedidaFisica",
        "UX_ProdutoCaracteristicaValores_Produto_Caracteristica_NomeNormalizado",
        "PK_ProdutoVarianteCaracteristicaValores",
        "UX_ProdutoImagens_ChaveArmazenamento",
        "UX_ProdutoImagens_ProdutoId_SlotGaleria",
        ImageMainIndex,
        "PK_ProdutoImagemVariantes"
    ];
    private static readonly string[] ProductCatalogForeignKeys =
    [
        "FK_ProdutoCaracteristicaValores_ProdutoCaracteristicas",
        "FK_ProdutoVarianteCaracteristicaValores_ProdutoCaracteristicaValores",
        "FK_ProdutoVarianteCaracteristicaValores_ProdutoCaracteristicas",
        "FK_ProdutoVarianteCaracteristicaValores_ProdutoVariantes",
        "FK_ProdutoImagens_Produtos_ProdutoId",
        "FK_ProdutoImagemVariantes_ProdutoImagens_ProdutoId_ProdutoImagemId",
        "FK_ProdutoImagemVariantes_ProdutoVariantes_ProdutoId_ProdutoVarianteId"
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
            .Include(produto => produto.Imagens)
                .ThenInclude(imagem => imagem.Variantes)
                    .ThenInclude(association => association.Variante)
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
        var execution = await ExecutarMutacaoComResultadoAsync(operacao, cancellationToken)
            .ConfigureAwait(false);
        if (execution.Failure is not null)
        {
            ExceptionDispatchInfo.Capture(execution.Failure).Throw();
        }

        return execution.Result
            ?? throw new InvalidOperationException("The product mutation did not return an operation result.");
    }

    public async Task<ProdutoMutationExecutionResult> ExecutarMutacaoComResultadoAsync(
        Func<IProdutoMutationSession, CancellationToken, Task<ProdutoResult>> operacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction;
        try
        {
            transaction = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return ProdutoMutationExecutionResult.Failed(
                ProdutoMutationPersistenceState.Unknown,
                exception);
        }

        await using (transaction.ConfigureAwait(false))
        {
            var committed = false;
            try
            {
                ProdutoResult result;
                try
                {
                    var session = new ProdutoMutationSession(_dbContext);
                    result = await operacao(session, cancellationToken).ConfigureAwait(false);
                    if (result.Status == ProdutoStatus.Success)
                    {
                        var variantesDesativadas = _dbContext.ChangeTracker.Entries<ProdutoVariante>()
                            .Where(entry => entry.State == EntityState.Modified
                                && entry.Property(variante => variante.Ativa).OriginalValue && !entry.Entity.Ativa)
                            .Select(entry => entry.Entity.Id)
                            .ToArray();
                        await SaveChangesWithUniqueSwitchesAsync(cancellationToken).ConfigureAwait(false);

                        // O UPDATE mantém o bloqueio exclusivo da variante até o fim da transação.
                        if (variantesDesativadas.Length > 0
                            && await PossuiReservasComprometidasAsync(variantesDesativadas, cancellationToken)
                                .ConfigureAwait(false))
                        {
                            return await RollbackWithResultAsync(
                                ProdutoResult.Conflict("Não é possível desativar uma variante com reservas comerciais comprometidas."),
                                transaction).ConfigureAwait(false);
                        }
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    return await RollbackWithResultAsync(PersistenceConflict(), transaction)
                        .ConfigureAwait(false);
                }
                catch (DbUpdateException exception) when (IsPersistenceConflict(exception))
                {
                    return await RollbackWithResultAsync(PersistenceConflict(), transaction)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException exception) when (IsPersistenceConflict(exception))
                {
                    return await RollbackWithResultAsync(PersistenceConflict(), transaction)
                        .ConfigureAwait(false);
                }
                catch (SqlException exception) when (IsPersistenceConflict(exception))
                {
                    return await RollbackWithResultAsync(PersistenceConflict(), transaction)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    return await RollbackWithFailureAsync(exception, transaction)
                        .ConfigureAwait(false);
                }

                if (result.Status != ProdutoStatus.Success)
                {
                    return await RollbackWithResultAsync(result, transaction)
                        .ConfigureAwait(false);
                }

                try
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    committed = true;
                    return ProdutoMutationExecutionResult.Committed(result);
                }
                catch (Exception exception)
                {
                    return ProdutoMutationExecutionResult.Failed(
                        ProdutoMutationPersistenceState.Unknown,
                        exception);
                }
            }
            finally
            {
                if (!committed)
                {
                    _dbContext.ChangeTracker.Clear();
                }
            }
        }
    }

    private async Task<bool> PossuiReservasComprometidasAsync(Guid[] variantes, CancellationToken cancellationToken)
    {
        var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        return await _dbContext.ReservasProdutoAcabado.ComprometidasEm(agora)
            .AnyAsync(reserva => variantes.Contains(reserva.ProdutoVarianteId), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ProdutoMutationExecutionResult> RollbackWithResultAsync(
        ProdutoResult result,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return ProdutoMutationExecutionResult.RolledBack(result);
        }
        catch (Exception rollbackException)
        {
            return ProdutoMutationExecutionResult.Failed(
                ProdutoMutationPersistenceState.Unknown,
                rollbackException);
        }
    }

    private static async Task<ProdutoMutationExecutionResult> RollbackWithFailureAsync(
        Exception failure,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return ProdutoMutationExecutionResult.Failed(
                ProdutoMutationPersistenceState.RolledBack,
                failure);
        }
        catch (Exception rollbackException)
        {
            return ProdutoMutationExecutionResult.Failed(
                ProdutoMutationPersistenceState.Unknown,
                new AggregateException(
                    "The product mutation failed and its transaction rollback could not be confirmed.",
                    failure,
                    rollbackException));
        }
    }

    private async Task SaveChangesWithUniqueSwitchesAsync(CancellationToken cancellationToken)
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

        var demotedMainImages = _dbContext.ChangeTracker
            .Entries<ProdutoImagem>()
            .Where(entry => entry.Property(image => image.Principal).OriginalValue
                && (entry.State == EntityState.Deleted || !entry.Entity.Principal))
            .ToArray();

        foreach (var entry in demotedMainImages)
        {
            var rowsUpdated = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [ProdutoImagens] SET [Principal] = CAST(0 AS bit) WHERE [Id] = {entry.Entity.Id} AND [ProdutoId] = {entry.Entity.ProdutoId} AND [Principal] = CAST(1 AS bit)",
                    cancellationToken)
                .ConfigureAwait(false);
            if (rowsUpdated != 1)
            {
                throw new DbUpdateConcurrencyException(
                    "The stored main product image changed during the product update.");
            }

            if (entry.State == EntityState.Modified)
            {
                var mainProperty = entry.Property(image => image.Principal);
                mainProperty.OriginalValue = false;
                mainProperty.IsModified = false;
            }
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
            .Include(produto => produto.Imagens)
                .ThenInclude(imagem => imagem.Variantes)
                    .ThenInclude(association => association.Variante)
            .AsSplitQuery();
}
