using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class EstoqueProdutoAcabadoOperacoesSaidaSqlServerIntegrationTests
{
    private static readonly DateOnly DataReferencia = new(2026, 11, 17);
    private static readonly Guid ResponsavelId =
        Guid.Parse("280282b9-a942-4625-981e-b30b20ce5104");

    [SqlServerIntegrationFact]
    public async Task Operations_LossDiscardsAndTransfersKeepAuditAndDerivedBalances()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.Add(
            PoliticaEstoqueProdutoVariante.Criar(variante, estoqueMinimo: 3, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var opening = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EstoqueInicial,
            10, CondicaoEstoqueProdutoAcabado.Vendavel, "Abertura", "opening",
            DataFabricacaoNovoLote: DataReferencia));
        Assert.True(opening.Sucesso, opening.Erro);
        var loteId = opening.Lote!.Id;

        var manualDamage = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EntradaManual,
            3, CondicaoEstoqueProdutoAcabado.Danificada, "Entrada danificada", "damage-entry",
            loteId));
        Assert.True(manualDamage.Sucesso, manualDamage.Erro);

        var manualQuarantine = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EntradaManual,
            2, CondicaoEstoqueProdutoAcabado.Quarentena, "Entrada em quarentena", "quarantine-entry",
            loteId));
        Assert.True(manualQuarantine.Sucesso, manualQuarantine.Erro);

        var saida = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.SaidaManual,
            2, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Venda administrativa", "sale"));
        Assert.True(saida.Sucesso, saida.Erro);

        var perda = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.Perda,
            1, CondicaoEstoqueProdutoAcabado.Danificada, null, "Quebra", "loss"));
        Assert.True(perda.Sucesso, perda.Erro);

        var descarteDanificado = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.Descarte,
            1, CondicaoEstoqueProdutoAcabado.Danificada, null, "Descarte danificado", "discard-damaged"));
        Assert.True(descarteDanificado.Sucesso, descarteDanificado.Erro);

        var descarteQuarentena = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.Descarte,
            1, CondicaoEstoqueProdutoAcabado.Quarentena, null, "Descarte em quarentena", "discard-quarantine"));
        Assert.True(descarteQuarentena.Sucesso, descarteQuarentena.Erro);

        var paraQuarentena = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            2, CondicaoEstoqueProdutoAcabado.Vendavel, CondicaoEstoqueProdutoAcabado.Quarentena,
            "Bloqueio preventivo", "to-quarantine"));
        Assert.True(paraQuarentena.Sucesso, paraQuarentena.Erro);

        var liberar = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            1, CondicaoEstoqueProdutoAcabado.Quarentena, CondicaoEstoqueProdutoAcabado.Vendavel,
            "Quarentena liberada", "release"));
        Assert.True(liberar.Sucesso, liberar.Erro);

        var danificar = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, CondicaoEstoqueProdutoAcabado.Danificada,
            "Produto danificado", "to-damage"));
        Assert.True(danificar.Sucesso, danificar.Erro);

        var recuperar = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            1, CondicaoEstoqueProdutoAcabado.Danificada, CondicaoEstoqueProdutoAcabado.Vendavel,
            "Inspeção aprovada", "to-sale"));
        Assert.True(recuperar.Sucesso, recuperar.Erro);

        context.ChangeTracker.Clear();
        var saldo = await context.SaldosProdutoAcabado.SingleAsync();
        Assert.Equal(10, saldo.QuantidadeFisica);
        Assert.Equal(7, saldo.QuantidadeVendavel);
        Assert.Equal(1, saldo.QuantidadeDanificada);
        Assert.Equal(2, saldo.QuantidadeQuarentena);

        var replay = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.Perda,
            1, CondicaoEstoqueProdutoAcabado.Danificada, null, "Quebra", "loss"));
        Assert.True(replay.Sucesso, replay.Erro);
        Assert.True(replay.Repetida);

        var conflito = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.Perda,
            2, CondicaoEstoqueProdutoAcabado.Danificada, null, "Quebra alterada", "loss"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.ConflitoIdempotencia, conflito.Status);
        Assert.Equal(11, await context.MovimentosProdutoAcabado.CountAsync());

        var consulta = database.Services.GetRequiredService<IEstoqueProdutoAcabadoConsultaRepository>();
        var detalhe = await consulta.ConsultarPosicaoVarianteAsync(variante.Id, DataReferencia);
        Assert.NotNull(detalhe);
        Assert.Equal(10, detalhe!.Resumo.SaldoFisico);
        Assert.Equal(7, detalhe.Resumo.SaldoDisponivel);
    }

    [SqlServerIntegrationFact]
    public async Task Operations_RejectInvalidLotExpiredInactiveAndInsufficientBalances()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        var outraVariante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.AddRange(
            PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 30),
            PoliticaEstoqueProdutoVariante.Criar(outraVariante, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var opening = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EstoqueInicial,
            2, CondicaoEstoqueProdutoAcabado.Vendavel, "Abertura", "opening-valid",
            DataFabricacaoNovoLote: DataReferencia));
        Assert.True(opening.Sucesso, opening.Erro);

        var stale = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, opening.Lote!.Id, TipoMovimentoProdutoAcabado.SaidaManual,
            3, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Saldo insuficiente", "too-much"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, stale.Status);

        var wrongLot = await repository.RegistrarOperacaoAsync(Operation(
            outraVariante.Id, opening.Lote.Id, TipoMovimentoProdutoAcabado.Perda,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Lote incorreto", "wrong-lot"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.LoteDeOutraVariante, wrongLot.Status);

        var wrongVariantLot = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            outraVariante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EstoqueInicial,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, "Abertura", "other-opening",
            DataFabricacaoNovoLote: DataReferencia));
        Assert.True(wrongVariantLot.Sucesso, wrongVariantLot.Erro);
        var foreign = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, wrongVariantLot.Lote!.Id, TipoMovimentoProdutoAcabado.Perda,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Variante incorreta", "foreign-lot"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.LoteDeOutraVariante, foreign.Status);

        var expired = await repository.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, ResponsavelId, TipoMovimentoProdutoAcabado.EntradaManual,
            2, CondicaoEstoqueProdutoAcabado.Vendavel, "Lote vencido", "expired-entry",
            DataFabricacaoNovoLote: DataReferencia.AddDays(-40),
            DataValidadeNovoLote: DataReferencia.AddDays(-1)));
        Assert.True(expired.Sucesso, expired.Erro);
        var expiredSale = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, expired.Lote!.Id, TipoMovimentoProdutoAcabado.SaidaManual,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Saída vencida", "expired-sale"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.LoteInelegivel, expiredSale.Status);

        var expiredDiscard = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, expired.Lote.Id, TipoMovimentoProdutoAcabado.Descarte,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Descarte vencido", "expired-discard"));
        Assert.True(expiredDiscard.Sucesso, expiredDiscard.Erro);

        var sameCondition = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, opening.Lote.Id, TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, CondicaoEstoqueProdutoAcabado.Vendavel,
            "Transferência inválida", "same-condition"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.OperacaoInvalida, sameCondition.Status);

        var transferAbove = await repository.RegistrarOperacaoAsync(Operation(
            variante.Id, opening.Lote.Id, TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            99, CondicaoEstoqueProdutoAcabado.Vendavel, CondicaoEstoqueProdutoAcabado.Quarentena,
            "Transferência excessiva", "above-transfer"));
        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, transferAbove.Status);
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentOperations_PreventDoubleConsumptionAndSerializeTransfer()
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
            10, CondicaoEstoqueProdutoAcabado.Vendavel, "Abertura", "open",
            DataFabricacaoNovoLote: DataReferencia));
        Assert.True(opening.Sucesso, opening.Erro);

        await using var scopeA = database.Services.CreateAsyncScope();
        await using var scopeB = database.Services.CreateAsyncScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var saleA = Operation(
            variante.Id, opening.Lote!.Id, TipoMovimentoProdutoAcabado.SaidaManual,
            7, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Saída A", "sale-a");
        var saleB = Operation(
            variante.Id, opening.Lote.Id, TipoMovimentoProdutoAcabado.SaidaManual,
            7, CondicaoEstoqueProdutoAcabado.Vendavel, null, "Saída B", "sale-b");
        var results = await Task.WhenAll(
            repoA.RegistrarOperacaoAsync(saleA),
            repoB.RegistrarOperacaoAsync(saleB));

        Assert.Single(results, item => item.Sucesso);
        Assert.Single(results, item =>
            item.Status is OperacaoProdutoAcabadoStatus.SaldoInsuficiente
                or OperacaoProdutoAcabadoStatus.ConflitoConcorrencia);
        context.ChangeTracker.Clear();
        var saldo = await context.SaldosProdutoAcabado.SingleAsync();
        Assert.True(saldo.QuantidadeVendavel is 3 or 10);

        var transfer = Operation(
            variante.Id, opening.Lote.Id, TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            1, CondicaoEstoqueProdutoAcabado.Vendavel, CondicaoEstoqueProdutoAcabado.Quarentena,
            "Quarentena concorrente", "transfer");
        var transferResult = await repoA.RegistrarOperacaoAsync(transfer);
        Assert.True(transferResult.Sucesso, transferResult.Erro);
        context.ChangeTracker.Clear();
        var finalBalance = await context.SaldosProdutoAcabado.SingleAsync();
        Assert.Equal(finalBalance.QuantidadeVendavel + finalBalance.QuantidadeDanificada
            + finalBalance.QuantidadeQuarentena, finalBalance.QuantidadeFisica);
    }

    private static RegistrarOperacaoProdutoAcabadoInput Operation(
        Guid varianteId,
        Guid loteId,
        TipoMovimentoProdutoAcabado tipo,
        int quantidade,
        CondicaoEstoqueProdutoAcabado? origem,
        CondicaoEstoqueProdutoAcabado? destino,
        string motivo,
        string chave) =>
        new(varianteId, loteId, ResponsavelId, tipo, quantidade, origem, destino, motivo, chave);

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
