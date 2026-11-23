using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class ExclusaoCategoriaSqlTests
{
    [SqlServerIntegrationFact]
    public async Task ExclusaoFisica_PreservaAuditoria_ReplayENomeReutilizavel()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var categoria = await CriarCategoria(db, "Categoria original");
        var autor = Guid.NewGuid();
        var chave = Guid.NewGuid();

        Assert.Equal(ExclusaoCategoriaStatus.Excluida, await Excluir(banco, categoria, autor, chave));
        Assert.Equal(ExclusaoCategoriaStatus.Excluida, await Excluir(banco, categoria, autor, chave));
        Assert.Equal(ExclusaoCategoriaStatus.Ausente, await Excluir(banco, categoria, autor, Guid.NewGuid()));
        db.ChangeTracker.Clear();
        Assert.False(await db.Categorias.AnyAsync());
        var nome = await db.Database.SqlQuery<string>($"""
            SELECT NomeCategoria AS Value FROM OperacoesExclusaoCategoria
            WHERE AutorId = {autor} AND Chave = {chave} AND CategoriaId = {categoria.Id} AND Resultado = 'Excluida'
            """).SingleAsync();
        Assert.Equal(categoria.Nome, nome);
        var nova = await CriarCategoria(db, categoria.Nome);
        Assert.NotEqual(categoria.Id, nova.Id);
        Assert.Equal(ExclusaoCategoriaStatus.ChaveEmUso, await Excluir(banco, nova, autor, chave));
        Assert.True(await db.Categorias.AnyAsync(x => x.Id == nova.Id));
    }

    [SqlServerIntegrationFact]
    public async Task CriacaoDeProdutoConcorrente_AposVerificacaoDeVinculo_EBloqueadaPelaFkRestritiva()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var categoria = await CriarCategoria(db, "Categoria concorrente");
        var pausa = new PausaAntesDaGravacao();
        var exclusao = Excluir(banco, categoria, Guid.NewGuid(), Guid.NewGuid(), pausa);
        try
        {
            await pausa.Recebida.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var vinculo = banco.CreateVerificationContext();
            var destino = await vinculo.Categorias.SingleAsync(x => x.Id == categoria.Id);
            vinculo.Produtos.Add(Produto.CriarSimples("Produto sem estoque", null, destino, "UN", 1m));
            await vinculo.SaveChangesAsync();
        }
        finally
        {
            pausa.Continuar.TrySetResult();
        }

        Assert.Equal(ExclusaoCategoriaStatus.ComProdutos, await exclusao);
        Assert.Equal(categoria.Id, await db.Produtos.Select(x => x.CategoriaId).SingleAsync());
        Assert.True(await db.Categorias.AnyAsync(x => x.Id == categoria.Id));
        Assert.Equal(0, await ContarAuditoria(db));
    }

    [SqlServerIntegrationFact]
    public async Task EdicaoConcorrente_AposValidacao_RenovaVersaoESobrevive()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var categoria = await CriarCategoria(db, "Antes");
        var pausa = new PausaAntesDaGravacao();
        var exclusao = Excluir(banco, categoria, Guid.NewGuid(), Guid.NewGuid(), pausa);
        try
        {
            await pausa.Recebida.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var edicao = banco.CreateVerificationContext();
            var resultado = await new GerenciarCategorias(new EfCategoriaRepository(edicao))
                .EditarAsync(categoria.Id, new CategoriaInput("Depois", null, 0, categoria.Versao));
            Assert.Equal(CategoriaStatus.Success, resultado.Status);
        }
        finally
        {
            pausa.Continuar.TrySetResult();
        }

        Assert.Equal(ExclusaoCategoriaStatus.VersaoDesatualizada, await exclusao);
        var atual = await db.Categorias.AsNoTracking().SingleAsync();
        Assert.NotEqual(categoria.Versao, atual.Versao);
        Assert.Equal("Depois", atual.Nome);
        Assert.Equal(0, await ContarAuditoria(db));
    }

    [SqlServerIntegrationFact]
    public async Task DuasExclusoesConcorrentes_ProduzemUmEventoEReplayRecuperaResultado()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var categoria = await CriarCategoria(db, "Categoria dupla");
        var autor = Guid.NewGuid();
        var chave = Guid.NewGuid();
        var pausa = new PausaAntesDaGravacao();
        var primeira = Excluir(banco, categoria, autor, chave, pausa);
        Task<ExclusaoCategoriaStatus> segunda;
        try
        {
            await pausa.Recebida.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var bloqueio = new ObservadorBloqueioCategoria(categoria.Id);
            segunda = Excluir(banco, categoria, autor, Guid.NewGuid(), bloqueio);
            await bloqueio.Solicitado.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            pausa.Continuar.TrySetResult();
        }

        Assert.Equal(ExclusaoCategoriaStatus.Excluida, await primeira);
        Assert.Equal(ExclusaoCategoriaStatus.Ausente, await segunda);
        Assert.Equal(ExclusaoCategoriaStatus.Excluida, await Excluir(banco, categoria, autor, chave));
        Assert.Equal(1, await ContarAuditoria(db));
    }

    [SqlServerIntegrationFact]
    public async Task FalhaAposGravacao_ReverteExclusaoEAuditoria()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var categoria = await CriarCategoria(db, "Categoria preservada");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Excluir(banco, categoria, Guid.NewGuid(), Guid.NewGuid(), new FalhaAposGravacao()));
        Assert.True(await db.Categorias.AnyAsync(x => x.Id == categoria.Id));
        Assert.Equal(0, await ContarAuditoria(db));
    }

    [SqlServerIntegrationFact]
    public async Task Migration_PreencheVersaoSemAlterarCategoriaEProdutoHistoricos()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var categoria = await CriarCategoria(db, "Categoria histórica");
        var produto = Produto.CriarSimples("Produto histórico", null, categoria, "UN", 1m);
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261123225109_CongelarDescontoFretePostal");
        await migrator.MigrateAsync();
        var atual = await db.Categorias.AsNoTracking().SingleAsync();
        Assert.Equal(categoria.Id, atual.Id);
        Assert.Equal(categoria.Nome, atual.Nome);
        Assert.NotEqual(Guid.Empty, atual.Versao);
        Assert.Equal(categoria.Id, await db.Produtos.Where(p => p.Id == produto.Id).Select(p => p.CategoriaId).SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RecategorizacaoConcorrente_AposVerificacao_ConservaProdutoECategoria()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var destino = await CriarCategoria(db, "Destino");
        var origem = await CriarCategoria(db, "Origem");
        var produto = Produto.CriarSimples("Produto recategorizado", null, origem, "UN", 1m);
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        var pausa = new PausaAntesDaGravacao();
        var exclusao = Excluir(banco, destino, Guid.NewGuid(), Guid.NewGuid(), pausa);
        try
        {
            await pausa.Recebida.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var vinculo = banco.CreateVerificationContext();
            var produtoAtual = await vinculo.Produtos.SingleAsync(x => x.Id == produto.Id);
            produtoAtual.AlterarCategoria(await vinculo.Categorias.SingleAsync(x => x.Id == destino.Id));
            await vinculo.SaveChangesAsync();
        }
        finally
        {
            pausa.Continuar.TrySetResult();
        }

        Assert.Equal(ExclusaoCategoriaStatus.ComProdutos, await exclusao);
        Assert.Equal(destino.Id, await db.Produtos.Select(x => x.CategoriaId).SingleAsync());
        Assert.Equal(0, await ContarAuditoria(db));
    }

    private static Task<int> ContarAuditoria(XaquiamuniDbContext db) =>
        db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM OperacoesExclusaoCategoria").SingleAsync();

    private static async Task<Categoria> CriarCategoria(XaquiamuniDbContext db, string nome)
    {
        var categoria = Categoria.Criar(nome, null, 0);
        db.Categorias.Add(categoria);
        await db.SaveChangesAsync();
        return categoria;
    }

    private static async Task<ExclusaoCategoriaStatus> Excluir(
        SqlServerTestDatabase banco, Categoria categoria, Guid autor, Guid chave, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        await using var db = new XaquiamuniDbContext(options.Options);
        return await new EfCategoriaRepository(db).ExecutarAsync(categoria.Id, categoria.Versao, autor, chave, default);
    }

    private sealed class PausaAntesDaGravacao : SaveChangesInterceptor
    {
        public TaskCompletionSource Recebida { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Recebida.TrySetResult();
            await Continuar.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken);
            return result;
        }
    }

    private sealed class ObservadorBloqueioCategoria(Guid categoriaId) : DbCommandInterceptor
    {
        public TaskCompletionSource Solicitado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, $"category-delete:{categoriaId:D}")))
                Solicitado.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FalhaAposGravacao : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha de teste antes do commit.");
    }
}

