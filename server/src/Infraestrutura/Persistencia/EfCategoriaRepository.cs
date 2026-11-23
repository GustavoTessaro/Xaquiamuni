using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfCategoriaRepository : ICategoriaRepository, IExcluirCategoria
{
    private const string NomeIndexName = "UX_Categorias_NomeNormalizado";

    private readonly XaquiamuniDbContext _dbContext;
    private readonly TimeProvider _relogio;

    public EfCategoriaRepository(XaquiamuniDbContext dbContext, TimeProvider? relogio = null)
    {
        _dbContext = dbContext;
        _relogio = relogio ?? TimeProvider.System;
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
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.Entry(categoria).State = EntityState.Detached;
            throw new CategoriaVersaoException();
        }
        catch (DbUpdateException exception) when (IsCategoryNameConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.Entry(categoria).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<ExclusaoCategoriaStatus> ExecutarAsync(Guid id, Guid versao, Guid autor, Guid chave, CancellationToken ct)
    {
        try
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await BloquearAsync($"category-delete-key:{autor:D}:{chave:D}", ct);
            var anterior = await _dbContext.Set<OperacaoExclusaoCategoria>().AsNoTracking().SingleOrDefaultAsync(
                x => x.AutorId == autor && x.Chave == chave, ct);
            if (anterior is not null)
                return anterior.CategoriaId == id && anterior.VersaoEsperada == versao
                    ? anterior.Resultado : ExclusaoCategoriaStatus.ChaveEmUso;

            await BloquearAsync($"category-delete:{id:D}", ct);
            var categoria = await _dbContext.Categorias.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (categoria is null) return ExclusaoCategoriaStatus.Ausente;
            if (categoria.Versao != versao) return ExclusaoCategoriaStatus.VersaoDesatualizada;
            if (await _dbContext.Produtos.AnyAsync(x => x.CategoriaId == id, ct))
                return ExclusaoCategoriaStatus.ComProdutos;

            _dbContext.Add(new OperacaoExclusaoCategoria(autor, chave, id, categoria.Nome, versao,
                _relogio.GetUtcNow(), ExclusaoCategoriaStatus.Excluida));
            _dbContext.Categorias.Remove(categoria);
            await _dbContext.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return ExclusaoCategoriaStatus.Excluida;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ExclusaoCategoriaStatus.VersaoDesatualizada;
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 547 } sql &&
            sql.Message.Contains("FK_Produtos_Categorias_CategoriaId", StringComparison.Ordinal))
        {
            return ExclusaoCategoriaStatus.ComProdutos;
        }
        catch (Exception e) when (e is SqlException { Number: 1205 or 1222 or 51000 }
            or DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 } })
        {
            return ExclusaoCategoriaStatus.ConflitoConcorrencia;
        }
    }

    private Task<int> BloquearAsync(string recurso, CancellationToken ct) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @status int;
            EXEC @status = sys.sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @status < 0 THROW 51000, 'Category deletion lock unavailable.', 1;
            """, ct);

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
