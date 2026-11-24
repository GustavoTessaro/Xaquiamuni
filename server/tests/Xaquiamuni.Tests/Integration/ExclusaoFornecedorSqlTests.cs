using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class ExclusaoFornecedorSqlTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly TimeProvider Relogio = TimeProvider.System;

    [SqlServerIntegrationFact]
    public async Task ExclusaoFornecedor_RegistraTrocaCancelamentoELegado()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (a, b, insumo) = await SemearAsync(db);
        var compras = new EfCompraMateriaPrimaRepository(db, Relogio);
        var criada = await compras.CriarAsync(Entrada(a.Id, insumo), Admin, Guid.NewGuid(), default);
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso, criada.Status);
        var compra = criada.Compra!;
        Assert.Equal(FornecedorStatus.ComCompras, await ExcluirAsync(banco, a));
        await using (var direto = banco.CreateVerificationContext())
        {
            var violacao = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
                direto.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Fornecedores WHERE Id = {a.Id}"));
            Assert.Equal(547, violacao.Number);
        }
        var editada = await compras.EditarAsync(compra.Id, Entrada(b.Id, insumo) with { Versao = compra.Versao }, Admin, default);
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso, editada.Status);
        Assert.Equal(FornecedorStatus.ComHistorico, await ExcluirAsync(banco, a));
        Assert.Equal(FornecedorStatus.ComCompras, await ExcluirAsync(banco, b));
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso,
            (await compras.CancelarAsync(compra.Id, compra.Versao, Admin, default)).Status);
        Assert.Equal(FornecedorStatus.ComCompras, await ExcluirAsync(banco, b));
        Assert.Empty(await db.MovimentosInsumo.ToListAsync());

        await db.Database.MigrateAsync("20261123234034_VersionarEExcluirCategorias");
        await db.Database.MigrateAsync();
        var legados = await db.Fornecedores.AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.Null(legados[a.Id].UtilizadoEmCompra);
        Assert.True(legados[b.Id].UtilizadoEmCompra);
        Assert.Equal(FornecedorStatus.HistoricoInconclusivo, await ExcluirAsync(banco, legados[a.Id]));
        Assert.Equal(FornecedorStatus.ComCompras, await ExcluirAsync(banco, legados[b.Id]));
        Assert.Equal(b.Id, await db.ComprasMateriasPrimas.Select(x => x.FornecedorId).SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoFornecedor_RollbackReverteMarcacaoEAuditoria()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (a, _, insumo) = await SemearAsync(db);
        await using (var falha = Contexto(banco, new FalhaAntesDoCommit()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new EfCompraMateriaPrimaRepository(falha, Relogio)
                .CriarAsync(Entrada(a.Id, insumo), Admin, Guid.NewGuid(), default));
        }
        Assert.False(await db.ComprasMateriasPrimas.AnyAsync());
        Assert.False(await db.Fornecedores.Where(x => x.Id == a.Id).Select(x => x.UtilizadoEmCompra).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExcluirAsync(banco, a, new FalhaAntesDoCommit()));
        Assert.True(await db.Fornecedores.AnyAsync(x => x.Id == a.Id));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoFornecedor").SingleAsync());
        Assert.Equal(FornecedorStatus.Sucesso, await ExcluirAsync(banco, a));
    }

    [SqlServerIntegrationFact]
    public Task ExclusaoFornecedor_CompraAdquireLockPrimeiro() => DisputarAsync(compraPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoFornecedor_ExclusaoAdquireLockPrimeiro() => DisputarAsync(compraPrimeiro: false);

    [SqlServerIntegrationFact]
    public async Task ExclusaoFornecedor_DuasExclusoesGravamUmEvento()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (a, _, _) = await SemearAsync(db);
        var barreira = new BarreiraFornecedor(a.Id);
        var primeira = ExcluirAsync(banco, a, barreira);
        Task<FornecedorStatus> segunda;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = ExcluirAsync(banco, a, barreira);
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        Assert.Equal(FornecedorStatus.Sucesso, await primeira);
        Assert.Equal(FornecedorStatus.NaoEncontrado, await segunda);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoFornecedor").SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoFornecedor_SubstituicaoAdquireLockPrimeiro()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (a, b, insumo) = await SemearAsync(db);
        var compra = (await new EfCompraMateriaPrimaRepository(db, Relogio)
            .CriarAsync(Entrada(a.Id, insumo), Admin, Guid.NewGuid(), default)).Compra!;
        var barreira = new BarreiraFornecedor(b.Id);
        await using var escrita = Contexto(banco, barreira);
        var troca = new EfCompraMateriaPrimaRepository(escrita, Relogio)
            .EditarAsync(compra.Id, Entrada(b.Id, insumo) with { Versao = compra.Versao }, Admin, default);
        Task<FornecedorStatus> exclusao;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            exclusao = ExcluirAsync(banco, b, barreira);
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(exclusao.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso, (await troca).Status);
        Assert.Equal(FornecedorStatus.ComCompras, await exclusao);
        Assert.Equal(FornecedorStatus.ComHistorico, await ExcluirAsync(banco, a));
        Assert.Equal(b.Id, await db.ComprasMateriasPrimas.Select(c => c.FornecedorId).SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoFornecedor_EdicaoDuranteExclusaoPreservaFornecedor()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (a, _, _) = await SemearAsync(db);
        var pausa = new PausaAntesDaGravacao();
        var exclusao = ExcluirAsync(banco, a, pausa);
        try
        {
            await pausa.Validada.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await using var edicao = banco.CreateVerificationContext();
            var resultado = await new EfFornecedorRepository(edicao, Relogio)
                .EditarAsync(a.Id, new("Fornecedor atualizado", null, null, null), a.Versao, default);
            Assert.Equal(FornecedorStatus.Sucesso, resultado.Status);
        }
        finally
        {
            pausa.Prosseguir.TrySetResult();
        }
        Assert.Equal(FornecedorStatus.VersaoDesatualizada, await exclusao);
        Assert.Equal("Fornecedor atualizado", await db.Fornecedores.Where(x => x.Id == a.Id).Select(x => x.Nome).SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoFornecedor").SingleAsync());
    }

    private sealed class PausaAntesDaGravacao : SaveChangesInterceptor
    {
        public TaskCompletionSource Validada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Prosseguir { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Validada.TrySetResult();
            await Prosseguir.Task.WaitAsync(TimeSpan.FromSeconds(18), cancellationToken);
            return result;
        }
    }

    private static async Task DisputarAsync(bool compraPrimeiro)
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (a, _, insumo) = await SemearAsync(db);
        var barreira = new BarreiraFornecedor(a.Id);
        async Task<CompraMateriaPrimaResult> CriarAsync()
        {
            await using var escrita = Contexto(banco, barreira);
            return await new EfCompraMateriaPrimaRepository(escrita, Relogio)
                .CriarAsync(Entrada(a.Id, insumo), Admin, Guid.NewGuid(), default);
        }
        Task<FornecedorStatus> Excluir() => ExcluirAsync(banco, a, barreira);
        Task<CompraMateriaPrimaResult>? compra = compraPrimeiro ? CriarAsync() : null;
        Task<FornecedorStatus>? exclusao = compraPrimeiro ? null : Excluir();
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if (compraPrimeiro) exclusao = Excluir(); else compra = CriarAsync();
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(compraPrimeiro ? exclusao!.IsCompleted : compra!.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        Assert.Equal(compraPrimeiro ? CompraMateriaPrimaStatus.Sucesso : CompraMateriaPrimaStatus.Invalida, (await compra!).Status);
        Assert.Equal(compraPrimeiro ? FornecedorStatus.ComCompras : FornecedorStatus.Sucesso, await exclusao!);
        Assert.Equal(compraPrimeiro ? 1 : 0, await db.ComprasMateriasPrimas.CountAsync());
        Assert.Equal(compraPrimeiro, await db.Fornecedores.AnyAsync(x => x.Id == a.Id));
    }

    private static async Task<(Fornecedor A, Fornecedor B, Guid Insumo)> SemearAsync(XaquiamuniDbContext db)
    {
        var a = Fornecedor.Criar("Fornecedor A", null, null, null, Relogio.GetUtcNow());
        var b = Fornecedor.Criar("Fornecedor B", null, null, null, Relogio.GetUtcNow());
        var insumo = InsumoEstoque.Criar("Material", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        db.Fornecedores.AddRange(a, b);
        db.InsumosEstoque.Add(insumo);
        await db.SaveChangesAsync();
        return (a, b, insumo.Id);
    }

    private static CompraMateriaPrimaInput Entrada(Guid fornecedor, Guid insumo) =>
        new(fornecedor, new DateOnly(2026, 11, 23), [new(null, 1, 10, [new(insumo, null, 1, null)])], null);

    private static async Task<FornecedorStatus> ExcluirAsync(SqlServerTestDatabase banco, Fornecedor fornecedor,
        IInterceptor? interceptor = null)
    {
        await using var db = Contexto(banco, interceptor);
        return (await new EfFornecedorRepository(db, Relogio).ExcluirAsync(fornecedor.Id, fornecedor.Versao,
            Admin, Guid.NewGuid(), default)).Status;
    }

    private static XaquiamuniDbContext Contexto(SqlServerTestDatabase banco, IInterceptor? interceptor)
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    private sealed class FalhaAntesDoCommit : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Falha de teste.");
    }

    private sealed class BarreiraFornecedor(Guid id) : DbCommandInterceptor
    {
        private int _solicitacoes;
        public TaskCompletionSource PrimeiroAdquiriu { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SegundoSolicitou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Prosseguir { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (EhLock(command) && Interlocked.Increment(ref _solicitacoes) == 2) SegundoSolicitou.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (EhLock(command) && PrimeiroAdquiriu.TrySetResult())
                await Prosseguir.Task.WaitAsync(TimeSpan.FromSeconds(18), cancellationToken);
            return result;
        }

        private bool EhLock(DbCommand command) => command.Parameters.Cast<DbParameter>()
            .Any(p => Equals(p.Value, $"supplier-link:{id:D}"));
    }
}
