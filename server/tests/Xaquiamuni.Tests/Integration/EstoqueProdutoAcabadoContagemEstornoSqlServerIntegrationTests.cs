using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class EstoqueProdutoAcabadoContagemEstornoSqlServerIntegrationTests
{
    private static readonly DateOnly DataReferencia = new(2026, 11, 17);
    private static readonly Guid ResponsavelId =
        Guid.Parse("280282b9-a942-4625-981e-b30b20ce5104");

    [SqlServerIntegrationFact]
    public async Task Count_NoDifferenceCreatesMarkerAndRealCountUpdatesThreeConditionsAtomically()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.Add(
            PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var opening = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EstoqueInicial,
            5, CondicaoEstoqueProdutoAcabado.Vendavel, "Abertura", "opening-count",
            DataFabricacaoNovoLote: DataReferencia));
        Assert.True(opening.Sucesso, opening.Erro);

        context.ChangeTracker.Clear();
        var saldo = await context.SaldosProdutoAcabado.SingleAsync();
        var versao = await context.SaldosProdutoAcabado
            .Select(item => EF.Property<byte[]>(item, "Versao"))
            .SingleAsync();
        var noDifference = await repository.ContarAsync(new ContarProdutoAcabadoInput(
            variante.Id, opening.Lote!.Id, ResponsavelId,
            5, 0, 0, "Conferência", "count-no-difference", versao));
        Assert.True(noDifference.Sucesso, noDifference.Erro);
        Assert.True(noDifference.SemDiferenca);
        Assert.Empty(noDifference.Movimentos);
        Assert.Equal(1, await context.ContagensSemDiferencaProdutoAcabado.CountAsync());

        var noDifferenceReplay = await repository.ContarAsync(new ContarProdutoAcabadoInput(
            variante.Id, opening.Lote.Id, ResponsavelId,
            5, 0, 0, "Conferência", "count-no-difference", versao));
        Assert.True(noDifferenceReplay.Sucesso);
        Assert.True(noDifferenceReplay.Repetida);

        context.ChangeTracker.Clear();
        var versaoAtual = await context.SaldosProdutoAcabado
            .Select(item => EF.Property<byte[]>(item, "Versao"))
            .SingleAsync();
        var counted = await repository.ContarAsync(new ContarProdutoAcabadoInput(
            variante.Id, opening.Lote.Id, ResponsavelId,
            3, 2, 1, "Redistribuição física", "count-multi", versaoAtual));
        Assert.True(counted.Sucesso, counted.Erro);
        Assert.Equal(3, counted.Movimentos.Count);

        context.ChangeTracker.Clear();
        var finalBalance = await context.SaldosProdutoAcabado.SingleAsync();
        Assert.Equal(3, finalBalance.QuantidadeVendavel);
        Assert.Equal(2, finalBalance.QuantidadeDanificada);
        Assert.Equal(1, finalBalance.QuantidadeQuarentena);
        Assert.Equal(6, finalBalance.QuantidadeFisica);
        Assert.Equal(3, await context.MovimentosProdutoAcabado
            .CountAsync(item => item.Tipo == TipoMovimentoProdutoAcabado.AjusteInventario));

        var replay = await repository.ContarAsync(new ContarProdutoAcabadoInput(
            variante.Id, opening.Lote.Id, ResponsavelId,
            3, 2, 1, "Redistribuição física", "count-multi", versaoAtual));
        Assert.True(replay.Sucesso);
        Assert.True(replay.Repetida);

        var conflict = await repository.ContarAsync(new ContarProdutoAcabadoInput(
            variante.Id, opening.Lote.Id, ResponsavelId,
            4, 2, 1, "Payload diferente", "count-multi", versaoAtual));
        Assert.Equal(OperacaoProdutoAcabadoStatus.ConflitoIdempotencia, conflict.Status);

        var stale = await repository.ContarAsync(new ContarProdutoAcabadoInput(
            variante.Id, opening.Lote.Id, ResponsavelId,
            3, 2, 1, "Contagem desatualizada", "count-stale", versao));
        Assert.Equal(OperacaoProdutoAcabadoStatus.ConflitoConcorrencia, stale.Status);
    }

    [SqlServerIntegrationFact]
    public async Task Reversal_ReversesEntryAndRejectsDuplicateOrLaterMovement()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.Add(
            PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var entry = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EntradaManual,
            4, CondicaoEstoqueProdutoAcabado.Vendavel, "Entrada", "entry-reversal",
            DataFabricacaoNovoLote: DataReferencia));
        Assert.True(entry.Sucesso, entry.Erro);
        var original = entry.Movimento!;

        var reversal = await repository.EstornarAsync(new EstornarMovimentoProdutoAcabadoInput(
            original.Id, ResponsavelId, "Estorno da entrada", "reverse-entry"));
        Assert.True(reversal.Sucesso, reversal.Erro);
        Assert.Equal(original.Id, reversal.Movimento!.MovimentoOriginalId);

        context.ChangeTracker.Clear();
        var balance = await context.SaldosProdutoAcabado.SingleAsync();
        Assert.Equal(0, balance.QuantidadeFisica);

        var duplicate = await repository.EstornarAsync(new EstornarMovimentoProdutoAcabadoInput(
            original.Id, ResponsavelId, "Estorno duplicado", "reverse-entry-duplicate"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.MovimentoJaEstornado, duplicate.Status);

        var tryReversingReversal = await repository.EstornarAsync(new EstornarMovimentoProdutoAcabadoInput(
            reversal.Movimento.Id, ResponsavelId, "Estornar estorno", "reverse-reversal"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.MovimentoNaoEncontrado, tryReversingReversal.Status);

        var newEntry = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EntradaManual,
            3, CondicaoEstoqueProdutoAcabado.Vendavel, "Nova entrada", "entry-after-reversal",
            LoteId: original.LoteId));
        Assert.True(newEntry.Sucesso, newEntry.Erro);
        var laterReversal = await repository.EstornarAsync(new EstornarMovimentoProdutoAcabadoInput(
            newEntry.Movimento!.Id, ResponsavelId, "Estorno seguro", "reverse-latest"));
        Assert.True(laterReversal.Sucesso, laterReversal.Erro);

        context.ChangeTracker.Clear();
        Assert.Equal(0, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(4, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Reversal_RejectsMovementAfterOriginalAndPreservesAppendOnlyLedger()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.Add(
            PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var entry = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EntradaManual,
            5, CondicaoEstoqueProdutoAcabado.Vendavel, "Entrada", "entry-later",
            DataFabricacaoNovoLote: DataReferencia));
        Assert.True(entry.Sucesso, entry.Erro);

        var later = await repository.RegistrarOperacaoAsync(new RegistrarOperacaoProdutoAcabadoInput(
            variante.Id, entry.Lote!.Id, ResponsavelId, TipoMovimentoProdutoAcabado.SaidaManual,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Saída posterior", "later-sale"));
        Assert.True(later.Sucesso, later.Erro);

        var reversal = await repository.EstornarAsync(new EstornarMovimentoProdutoAcabadoInput(
            entry.Movimento!.Id, ResponsavelId, "Estorno conflitante", "reverse-conflict"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.ConflitoConcorrencia, reversal.Status);

        context.ChangeTracker.Clear();
        Assert.Equal(4, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [MovimentosProdutoAcabado] SET [Motivo] = 'alterado' WHERE [Id] = {entry.Movimento.Id}"));
    }

    private static async Task<ProdutoVariante> CreateVariantAsync(XaquiamuniDbContext context)
    {
        var category = Categoria.Criar($"Categoria {Guid.NewGuid():N}", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();
        var variant = ProdutoVariante.Criar("Padrão", "UN", 12m, padrao: true);
        var product = Produto.Criar($"Produto {Guid.NewGuid():N}", null, category, [variant]);
        context.Produtos.Add(product);
        await context.SaveChangesAsync();
        return variant;
    }
}
