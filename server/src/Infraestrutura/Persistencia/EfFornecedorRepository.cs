using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record ExclusaoFornecedor(Guid Autor, Guid Chave, Guid FornecedorId, string Nome,
    Guid Versao, DateTimeOffset InstanteUtc, string Resultado);

public sealed class EfFornecedorRepository(XaquiamuniDbContext db, TimeProvider relogio) : IGerenciarFornecedores
{
    internal static Task BloquearVinculoAsync(XaquiamuniDbContext db, Guid id, CancellationToken ct) =>
        BloquearAsync(db, $"supplier-link:{id:D}", ct);

    private static Task BloquearAsync(XaquiamuniDbContext db, string recurso, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @resultado < 0 THROW 51000, 'Supplier lock unavailable.', 1;
            """, ct);

    public async Task<IReadOnlyList<Fornecedor>> ListarAsync(string? busca, bool? ativo, CancellationToken ct)
    {
        var query = db.Fornecedores.AsNoTracking();
        var termo = Fornecedor.NormalizarNome(busca).ToUpperInvariant();
        if (termo.Length > 0) query = query.Where(f => f.NomeNormalizado.Contains(termo));
        if (ativo is bool filtro) query = query.Where(f => f.Ativo == filtro);
        return await query.OrderBy(f => f.NomeNormalizado).ThenBy(f => f.Id).ToListAsync(ct);
    }

    public Task<Fornecedor?> ConsultarAsync(Guid id, CancellationToken ct) =>
        db.Fornecedores.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);

    public Task<ResultadoFornecedor> CadastrarAsync(DadosFornecedor dados, CancellationToken ct) =>
        GravarAsync(() =>
        {
            var fornecedor = Fornecedor.Criar(dados.Nome, dados.Telefone, dados.Email, dados.Observacoes, relogio.GetUtcNow());
            db.Fornecedores.Add(fornecedor);
            return fornecedor;
        }, ct);

    public async Task<ResultadoFornecedor> EditarAsync(Guid id, DadosFornecedor dados, Guid versao, CancellationToken ct)
    {
        var fornecedor = await db.Fornecedores.FindAsync([id], ct);
        if (fornecedor is null) return new(FornecedorStatus.NaoEncontrado);
        if (fornecedor.Versao != versao) return new(FornecedorStatus.VersaoDesatualizada);
        return await GravarAsync(() =>
        {
            fornecedor.Editar(dados.Nome, dados.Telefone, dados.Email, dados.Observacoes, relogio.GetUtcNow());
            return fornecedor;
        }, ct);
    }

    public async Task<ResultadoFornecedor> AlterarStatusAsync(Guid id, bool ativo, CancellationToken ct)
    {
        var fornecedor = await db.Fornecedores.FindAsync([id], ct);
        if (fornecedor is null) return new(FornecedorStatus.NaoEncontrado);
        fornecedor.DefinirAtivo(ativo, relogio.GetUtcNow());
        return await GravarAsync(() => fornecedor, ct);
    }

    public async Task<ResultadoFornecedor> ExcluirAsync(Guid id, Guid versao, Guid autor, Guid chave, CancellationToken ct)
    {
        try
        {
            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            await BloquearAsync(db, $"supplier-delete:{autor:D}:{chave:D}", ct);
            var auditoria = db.Set<ExclusaoFornecedor>();
            var anterior = await auditoria.AsNoTracking().SingleOrDefaultAsync(x => x.Autor == autor && x.Chave == chave, ct);
            if (anterior is not null)
                return new(anterior.FornecedorId == id && anterior.Versao == versao
                    ? FornecedorStatus.Sucesso : FornecedorStatus.ChaveEmUso);
            await BloquearVinculoAsync(db, id, ct);
            var fornecedor = await db.Fornecedores.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (fornecedor is null) return new(FornecedorStatus.NaoEncontrado);
            if (fornecedor.Versao != versao) return new(FornecedorStatus.VersaoDesatualizada);
            if (await db.ComprasMateriasPrimas.AnyAsync(c => c.FornecedorId == id, ct))
                return new(FornecedorStatus.ComCompras);
            if (fornecedor.UtilizadoEmCompra != false)
                return new(fornecedor.UtilizadoEmCompra == true ? FornecedorStatus.ComHistorico : FornecedorStatus.HistoricoInconclusivo);

            auditoria.Add(new(autor, chave, id, fornecedor.Nome, versao, relogio.GetUtcNow(), "Excluido"));
            db.Fornecedores.Remove(fornecedor);
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return new(FornecedorStatus.Sucesso);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(FornecedorStatus.VersaoDesatualizada);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 547 } sql &&
            sql.Message.Contains("FK_ComprasMateriasPrimas_Fornecedores_FornecedorId", StringComparison.Ordinal))
        {
            return new(FornecedorStatus.ComCompras);
        }
        catch (Exception erro) when (ErroLock(erro))
        {
            return new(FornecedorStatus.ConflitoConcorrencia);
        }
    }

    internal static bool ErroLock(Exception erro) => erro is SqlException { Number: 1205 or 1222 or 51000 } or
        DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 } };

    private async Task<ResultadoFornecedor> GravarAsync(Func<Fornecedor> aplicar, CancellationToken ct)
    {
        try
        {
            var fornecedor = aplicar();
            await db.SaveChangesAsync(ct);
            return new(FornecedorStatus.Sucesso, fornecedor);
        }
        catch (ArgumentException erro)
        {
            return new(FornecedorStatus.Invalido, Erro: erro.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(FornecedorStatus.VersaoDesatualizada);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 2601 or 2627 } sql &&
            sql.Message.Contains("UX_Fornecedores_NomeNormalizado", StringComparison.Ordinal))
        {
            foreach (var entrada in erro.Entries) entrada.State = EntityState.Detached;
            return new(FornecedorStatus.NomeDuplicado, Erro: "Já existe um fornecedor com esse nome.");
        }
    }
}
