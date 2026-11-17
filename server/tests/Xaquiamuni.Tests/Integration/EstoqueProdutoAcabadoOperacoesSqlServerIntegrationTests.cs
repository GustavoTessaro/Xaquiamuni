using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class EstoqueProdutoAcabadoOperacoesSqlServerIntegrationTests
{
    private static readonly DateOnly DataReferencia = new(2026, 11, 17);
    private static readonly DateTimeOffset RegistradoEmUtc =
        new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid ResponsavelId =
        Guid.Parse("280282b9-a942-4625-981e-b30b20ce5104");

    [SqlServerIntegrationFact]
    public async Task Entry_CreatesLotPersistsMovementAndReturnsIdempotentResult()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.Add(
            PoliticaEstoqueProdutoVariante.Criar(variante, estoqueMinimo: 3, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var entrada = CreateEntry(
            variante.Id,
            "  abertura   inicial ",
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            quantidade: 5,
            dataFabricacao: DataReferencia);
        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var first = await repository.RegistrarEntradaAsync(entrada);
        Assert.True(first.Sucesso, first.Erro);
        Assert.NotNull(first.Movimento);
        Assert.NotNull(first.Lote);
        Assert.Equal(5, first.Movimento!.SaldoPosterior.QuantidadeVendavel);
        Assert.Equal(DataReferencia.AddDays(30), first.Lote!.DataValidade);
        Assert.Equal(30, first.Lote.PrazoValidadeAplicadoDias);

        var replay = await repository.RegistrarEntradaAsync(
            entrada with { ChaveIdempotencia = "abertura inicial" });
        Assert.True(replay.Sucesso, replay.Erro);
        Assert.True(replay.Repetida);
        Assert.Equal(first.Movimento.Id, replay.Movimento!.Id);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(1, await context.LotesProdutoAcabado.CountAsync());
        Assert.Equal(5, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);

        var conflito = await repository.RegistrarEntradaAsync(entrada with { Quantidade = 6 });
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.ConflitoIdempotencia, conflito.Status);

        var duplicadoInicial = await repository.RegistrarEntradaAsync(
            entrada with { ChaveIdempotencia = "outra-abertura", Quantidade = 1, LoteId = first.Lote.Id,
                DataFabricacaoNovoLote = null });
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.EstoqueInicialJaRegistrado, duplicadoInicial.Status);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Entry_ValidatesVariantLotDatesQuantityAndCondition()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        var outraVariante = await CreateVariantAsync(context);
        var politica = PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 10);
        context.PoliticasEstoqueProdutoVariante.Add(politica);
        var loteDeOutraVariante = LoteProdutoAcabado.Criar(
            outraVariante,
            PoliticaEstoqueProdutoVariante.Criar(outraVariante, prazoValidadePadraoDias: 10),
            DataReferencia);
        context.LotesProdutoAcabado.Add(loteDeOutraVariante);
        await context.SaveChangesAsync();

        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var zero = await repository.RegistrarEntradaAsync(CreateEntry(
            variante.Id, "zero", TipoMovimentoProdutoAcabado.EntradaManual, 0, dataFabricacao: DataReferencia));
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida, zero.Status);

        var negativo = await repository.RegistrarEntradaAsync(CreateEntry(
            variante.Id, "negativo", TipoMovimentoProdutoAcabado.EntradaManual, -1,
            dataFabricacao: DataReferencia));
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida, negativo.Status);

        var inexistente = await repository.RegistrarEntradaAsync(CreateEntry(
            Guid.NewGuid(), "variante-inexistente", TipoMovimentoProdutoAcabado.EntradaManual, 1,
            dataFabricacao: DataReferencia));
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.VarianteNaoEncontrada, inexistente.Status);

        var loteAlheio = await repository.RegistrarEntradaAsync(CreateEntry(
            variante.Id, "lote-alheio", TipoMovimentoProdutoAcabado.EntradaManual, 1,
            loteId: loteDeOutraVariante.Id));
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.LoteDeOutraVariante, loteAlheio.Status);

        var validadeAnterior = await repository.RegistrarEntradaAsync(CreateEntry(
            variante.Id, "validade-anterior", TipoMovimentoProdutoAcabado.EntradaManual, 1,
            dataFabricacao: DataReferencia, dataValidade: DataReferencia.AddDays(-1)));
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida, validadeAnterior.Status);

        var semPrazoOuValidade = await repository.RegistrarEntradaAsync(CreateEntry(
            outraVariante.Id, "sem-prazo", TipoMovimentoProdutoAcabado.EntradaManual, 1,
            dataFabricacao: DataReferencia));
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida, semPrazoOuValidade.Status);

        var condicaoInvalida = await repository.RegistrarEntradaAsync(CreateEntry(
            variante.Id, "condicao-invalida", TipoMovimentoProdutoAcabado.EntradaManual, 1,
            dataFabricacao: DataReferencia) with
        {
            CondicaoDestino = (CondicaoEstoqueProdutoAcabado)99
        });
        Assert.Equal(RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida, condicaoInvalida.Status);
        Assert.Equal(0, await context.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(1, await context.LotesProdutoAcabado.CountAsync());
        Assert.Equal(0, await context.SaldosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentEntries_SameKeyAreIdempotentAndDifferentKeysSerializeByVariant()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.Add(
            PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var opening = CreateEntry(
            variante.Id,
            "concorrente-inicial",
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            1,
            dataFabricacao: DataReferencia);
        var replayResults = await RunConcurrentlyAsync(database, opening, opening);
        Assert.All(replayResults, result => Assert.True(result.Sucesso, result.Erro));
        Assert.Single(replayResults, result => result.Repetida);
        var loteId = replayResults[0].Lote!.Id;

        var entradaA = CreateEntry(
            variante.Id, "concorrente-a", TipoMovimentoProdutoAcabado.EntradaManual, 2, loteId: loteId);
        var entradaB = CreateEntry(
            variante.Id, "concorrente-b", TipoMovimentoProdutoAcabado.EntradaManual, 3, loteId: loteId);
        var concurrentResults = await RunConcurrentlyAsync(database, entradaA, entradaB);
        Assert.All(concurrentResults, result => Assert.True(result.Sucesso, result.Erro));
        Assert.Equal(6, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Queries_DeriveAvailabilityByDateConditionAndVariantStateWithoutWrites()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        var varianteInativa = await CreateInactiveVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.AddRange(
            PoliticaEstoqueProdutoVariante.Criar(variante, estoqueMinimo: 3, prazoValidadePadraoDias: 30),
            PoliticaEstoqueProdutoVariante.Criar(varianteInativa, estoqueMinimo: 1, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();

        var entries = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var lotes = new Dictionary<string, Guid>();
        foreach (var (key, quantity, condition, expiry) in new[]
        {
            ("vendavel-valido", 2, CondicaoEstoqueProdutoAcabado.Vendavel, DataReferencia),
            ("danificado", 3, CondicaoEstoqueProdutoAcabado.Danificada, DataReferencia.AddDays(1)),
            ("quarentena", 4, CondicaoEstoqueProdutoAcabado.Quarentena, DataReferencia.AddDays(1)),
            ("vencido", 5, CondicaoEstoqueProdutoAcabado.Vendavel, DataReferencia.AddDays(-1))
        })
        {
            var result = await entries.RegistrarEntradaAsync(CreateEntry(
                variante.Id,
                key,
                TipoMovimentoProdutoAcabado.EntradaManual,
                quantity,
                condition,
                dataFabricacao: DataReferencia.AddDays(-10),
                dataValidade: expiry));
            Assert.True(result.Sucesso, result.Erro);
            lotes.Add(key, result.Lote!.Id);
        }

        var inativa = await entries.RegistrarEntradaAsync(CreateEntry(
            varianteInativa.Id,
            "variante-inativa",
            TipoMovimentoProdutoAcabado.EntradaManual,
            2,
            dataFabricacao: DataReferencia));
        Assert.True(inativa.Sucesso, inativa.Erro);

        var consulta = database.Services.GetRequiredService<IEstoqueProdutoAcabadoConsultaRepository>();
        var beforeMovements = await context.MovimentosProdutoAcabado.CountAsync();
        var beforeBalances = await context.SaldosProdutoAcabado
            .OrderBy(item => item.ProdutoVarianteId)
            .ThenBy(item => item.LoteId)
            .Select(item => new
            {
                item.ProdutoVarianteId,
                item.LoteId,
                item.QuantidadeVendavel,
                item.QuantidadeDanificada,
                item.QuantidadeQuarentena
            })
            .ToArrayAsync();

        var detail = await consulta.ConsultarPosicaoVarianteAsync(variante.Id, DataReferencia);
        Assert.NotNull(detail);
        Assert.Equal(14, detail!.Resumo.SaldoFisico);
        Assert.Equal(2, detail.Resumo.SaldoUtilizavel);
        Assert.Equal(0, detail.Resumo.SaldoReservado);
        Assert.Equal(2, detail.Resumo.SaldoDisponivel);
        Assert.Equal(3, detail.Resumo.EstoqueMinimo);
        Assert.Equal(SituacaoEstoqueProdutoAcabado.AbaixoOuNoMinimo, detail.Resumo.Situacao);
        Assert.Equal(4, detail.Lotes.Count);
        var vencimentoHoje = detail.Lotes.Single(item => item.LoteId == lotes["vendavel-valido"]);
        Assert.True(vencimentoHoje.Valido);
        Assert.Equal(2, vencimentoHoje.SaldoUtilizavel);
        var expiredAtNextDay = await consulta.ConsultarPosicaoLoteAsync(
            variante.Id,
            vencimentoHoje.LoteId,
            DataReferencia.AddDays(1));
        Assert.NotNull(expiredAtNextDay);
        Assert.False(expiredAtNextDay!.Valido);
        Assert.Equal(0, expiredAtNextDay.SaldoUtilizavel);

        var damaged = detail.Lotes.Single(item => item.LoteId == lotes["danificado"]);
        Assert.Equal(3, damaged.SaldoFisico);
        Assert.Equal(0, damaged.SaldoUtilizavel);
        var quarantined = detail.Lotes.Single(item => item.LoteId == lotes["quarentena"]);
        Assert.Equal(4, quarantined.SaldoFisico);
        Assert.Equal(0, quarantined.SaldoUtilizavel);

        var inactiveDetail = await consulta.ConsultarPosicaoVarianteAsync(varianteInativa.Id, DataReferencia);
        Assert.NotNull(inactiveDetail);
        Assert.Equal(2, inactiveDetail!.Resumo.SaldoFisico);
        Assert.Equal(2, inactiveDetail.Resumo.SaldoUtilizavel);
        Assert.Equal(0, inactiveDetail.Resumo.SaldoDisponivel);
        Assert.Equal(SituacaoEstoqueProdutoAcabado.SemEstoque, inactiveDetail.Resumo.Situacao);

        var page1 = await consulta.ListarMovimentosAsync(new ListarMovimentosProdutoAcabadoFiltro(
            variante.Id, ItensPorPagina: 2));
        var page2 = await consulta.ListarMovimentosAsync(new ListarMovimentosProdutoAcabadoFiltro(
            variante.Id, Pagina: 2, ItensPorPagina: 2));
        var filtered = await consulta.ListarMovimentosAsync(new ListarMovimentosProdutoAcabadoFiltro(
            variante.Id,
            Tipo: TipoMovimentoProdutoAcabado.EntradaManual,
            LoteId: lotes["vencido"]));
        Assert.Equal(4, page1.TotalItens);
        Assert.Equal(2, page1.Itens.Count);
        Assert.Equal(2, page2.Itens.Count);
        Assert.Equal(1, filtered.TotalItens);
        Assert.Equal(lotes["vencido"], Assert.Single(filtered.Itens).LoteId);

        Assert.Equal(beforeMovements, await context.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(
            beforeBalances,
            await context.SaldosProdutoAcabado
                .OrderBy(item => item.ProdutoVarianteId)
                .ThenBy(item => item.LoteId)
                .Select(item => new
                {
                    item.ProdutoVarianteId,
                    item.LoteId,
                    item.QuantidadeVendavel,
                    item.QuantidadeDanificada,
                    item.QuantidadeQuarentena
                })
                .ToArrayAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Entry_RollsBackLotAndBalanceWhenLedgerInsertFails()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        context.PoliticasEstoqueProdutoVariante.Add(
            PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 30));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [dbo].[TR_Test_FailFinishedGoodsMovement]
            ON [dbo].[MovimentosProdutoAcabado]
            AFTER INSERT
            AS
            BEGIN
                THROW 51001, 'Injected ledger failure for transaction test.', 1;
            END;
            """);

        var repository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.RegistrarEntradaAsync(
            CreateEntry(
                variante.Id,
                "rollback-ledger",
                TipoMovimentoProdutoAcabado.EntradaManual,
                4,
                dataFabricacao: DataReferencia)));

        await using var verification = database.CreateVerificationContext();
        Assert.Equal(0, await verification.LotesProdutoAcabado.CountAsync());
        Assert.Equal(0, await verification.SaldosProdutoAcabado.CountAsync());
        Assert.Equal(0, await verification.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task RowVersion_RejectsStaleConcurrentBalanceUpdate()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var seedContext = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(seedContext);
        var politica = PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 30);
        seedContext.PoliticasEstoqueProdutoVariante.Add(politica);
        await seedContext.SaveChangesAsync();

        var entryRepository = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var opening = await entryRepository.RegistrarEntradaAsync(CreateEntry(
            variante.Id,
            "rowversion-opening",
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            1,
            dataFabricacao: DataReferencia));
        Assert.True(opening.Sucesso, opening.Erro);

        await using var firstContext = database.CreateVerificationContext();
        await using var secondContext = database.CreateVerificationContext();
        var firstBalance = await firstContext.SaldosProdutoAcabado.SingleAsync();
        var secondBalance = await secondContext.SaldosProdutoAcabado.SingleAsync();
        var firstLot = await firstContext.LotesProdutoAcabado.SingleAsync();
        var secondLot = await secondContext.LotesProdutoAcabado.SingleAsync();

        var firstMovement = firstBalance.RegistrarEntrada(
            firstLot, 1, CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual, ResponsavelId, "Concorrência A",
            RegistradoEmUtc, "rowversion-a", Fingerprint());
        var secondMovement = secondBalance.RegistrarEntrada(
            secondLot, 1, CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual, ResponsavelId, "Concorrência B",
            RegistradoEmUtc, "rowversion-b", Fingerprint());
        firstContext.MovimentosProdutoAcabado.Add(firstMovement);
        secondContext.MovimentosProdutoAcabado.Add(secondMovement);

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());

        await using var verification = database.CreateVerificationContext();
        Assert.Equal(2, (await verification.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(2, await verification.MovimentosProdutoAcabado.CountAsync());
    }

    private static RegistrarEntradaProdutoAcabadoInput CreateEntry(
        Guid varianteId,
        string key,
        TipoMovimentoProdutoAcabado tipo,
        int quantidade,
        CondicaoEstoqueProdutoAcabado condition = CondicaoEstoqueProdutoAcabado.Vendavel,
        Guid? loteId = null,
        DateOnly? dataFabricacao = null,
        DateOnly? dataValidade = null) =>
        new(
            varianteId,
            ResponsavelId,
            tipo,
            quantidade,
            condition,
            "Recebimento administrativo",
            key,
            loteId,
            dataFabricacao,
            dataValidade);

    private static async Task<ProdutoVariante> CreateVariantAsync(
        XaquiamuniDbContext context)
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

    private static async Task<ProdutoVariante> CreateInactiveVariantAsync(XaquiamuniDbContext context)
    {
        var category = Categoria.Criar($"Categoria {Guid.NewGuid():N}", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();
        var active = ProdutoVariante.Criar("Padrão", "UN", 12m, padrao: true);
        var inactive = ProdutoVariante.Criar("Inativa", "UN", 15m, ativa: false);
        var product = Produto.Criar($"Produto {Guid.NewGuid():N}", null, category, [active, inactive]);
        context.Produtos.Add(product);
        await context.SaveChangesAsync();
        return inactive;
    }

    private static async Task<RegistrarEntradaProdutoAcabadoResult[]> RunConcurrentlyAsync(
        SqlServerTestDatabase database,
        RegistrarEntradaProdutoAcabadoInput first,
        RegistrarEntradaProdutoAcabadoInput second)
    {
        await using var firstScope = database.Services.CreateAsyncScope();
        await using var secondScope = database.Services.CreateAsyncScope();
        var firstRepository = firstScope.ServiceProvider
            .GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var secondRepository = secondScope.ServiceProvider
            .GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        return await Task.WhenAll(
            firstRepository.RegistrarEntradaAsync(first),
            secondRepository.RegistrarEntradaAsync(second));
    }

    private static byte[] Fingerprint() =>
        System.Security.Cryptography.SHA256.HashData("finished-goods-concurrency"u8);
}
