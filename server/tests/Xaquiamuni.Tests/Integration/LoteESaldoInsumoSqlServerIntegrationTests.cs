using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class LoteESaldoInsumoSqlServerIntegrationTests
{
    private const string MigrationId = "20261116171157_PersistirLotesESaldosInsumo";
    private const string PreviousMigrationId = "20261116165809_PersistirInsumosEFormasCompra";

    [SqlServerIntegrationFact]
    public async Task Migration_AddsOnlyLotAndBalanceTablesAndCanBeReversed()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        var tables = await ReadTableNamesAsync(context);

        Assert.Contains(MigrationId, appliedMigrations);
        Assert.Contains("InsumosEstoque", tables);
        Assert.Contains("FormasCompraInsumo", tables);
        Assert.Contains("LotesInsumo", tables);
        Assert.Contains("SaldosInsumo", tables);
        Assert.DoesNotContain(tables, name =>
            name.Contains("Movimento", StringComparison.Ordinal)
            || name.Contains("Reserva", StringComparison.Ordinal)
            || name.Contains("Custo", StringComparison.Ordinal));

        await context.GetService<IMigrator>().MigrateAsync(PreviousMigrationId);
        await using var downContext = database.CreateVerificationContext();
        var tablesAfterDown = await ReadTableNamesAsync(downContext);
        Assert.Contains("InsumosEstoque", tablesAfterDown);
        Assert.Contains("FormasCompraInsumo", tablesAfterDown);
        Assert.DoesNotContain("LotesInsumo", tablesAfterDown);
        Assert.DoesNotContain("SaldosInsumo", tablesAfterDown);

        await downContext.GetService<IMigrator>().MigrateAsync(MigrationId);
        await using var upContext = database.CreateVerificationContext();
        Assert.Contains(MigrationId, await upContext.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerIntegrationFact]
    public async Task PersistsAndReloadsLotsAndExplicitZeroBalancesWithoutImplicitStock()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var untracked = InsumoEstoque.Criar(
            "Cera agregada",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        var noBalance = InsumoEstoque.Criar(
            "Essência sem saldo",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro);
        var tracked = InsumoEstoque.Criar(
            "Base com lote",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            controlaLote: true,
            validadeObrigatoria: true);
        context.InsumosEstoque.AddRange(untracked, noBalance, tracked);
        await context.SaveChangesAsync();

        Assert.Empty(await context.LotesInsumo.ToListAsync());
        Assert.Empty(await context.SaldosInsumo.ToListAsync());

        var lot = LoteInsumo.Criar(
            tracked,
            "  LOTE   EXTERNO-01 ",
            new DateOnly(2026, 11, 16),
            new DateOnly(2027, 3, 10),
            "Fornecedor A");
        var sameCodeFromAnotherSupplier = LoteInsumo.Criar(
            tracked,
            "LOTE EXTERNO-01",
            new DateOnly(2026, 11, 17),
            new DateOnly(2027, 3, 11),
            "Fornecedor B");
        context.LotesInsumo.AddRange(lot, sameCodeFromAnotherSupplier);
        context.SaldosInsumo.AddRange(
            SaldoInsumo.CriarZero(untracked),
            SaldoInsumo.CriarZero(tracked, lot));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persistedLots = await context.LotesInsumo.OrderBy(item => item.DataEntrada).ToArrayAsync();
        var persistedBalances = await context.SaldosInsumo.ToArrayAsync();
        var persistedTracked = await context.InsumosEstoque.SingleAsync(item => item.Id == tracked.Id);

        Assert.Equal(2, persistedLots.Length);
        Assert.All(persistedLots, item => Assert.Equal("LOTE EXTERNO-01", item.Identificacao));
        Assert.Equal("Fornecedor A", persistedLots[0].Fornecedor);
        Assert.Equal(new DateOnly(2026, 11, 16), persistedLots[0].DataEntrada);
        Assert.Equal(new DateOnly(2027, 3, 10), persistedLots[0].DataValidade);
        Assert.Equal("Fornecedor B", persistedLots[1].Fornecedor);
        Assert.Equal(2, persistedBalances.Length);
        Assert.Contains(persistedBalances, item => item.InsumoId == untracked.Id && item.LoteId is null);
        Assert.Contains(persistedBalances, item => item.InsumoId == tracked.Id && item.LoteId == lot.Id);
        Assert.All(persistedBalances, item => Assert.Equal(0m, item.QuantidadeFisica.ValorBase));
        Assert.Equal(0m, persistedTracked.CalcularSaldoFisico(
            persistedBalances.Where(item => item.InsumoId == tracked.Id)).ValorBase);
        Assert.Empty(await context.SaldosInsumo.Where(item => item.InsumoId == noBalance.Id).ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task PhysicalTotalIsDerivedFromLotBalancesIncludingExpiredAndBlockedStock()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var insumo = InsumoEstoque.Criar(
            "Óleo essencial",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true,
            validadeObrigatoria: true);
        var expiredLot = LoteInsumo.Criar(
            insumo,
            "OLEO-1",
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 11, 15));
        var blockedLot = LoteInsumo.Criar(
            insumo,
            "OLEO-2",
            new DateOnly(2026, 10, 2),
            new DateOnly(2026, 12, 1));
        context.InsumosEstoque.Add(insumo);
        context.LotesInsumo.AddRange(expiredLot, blockedLot);
        await context.SaveChangesAsync();

        await InsertBalanceForTestAsync(context, insumo.Id, expiredLot.Id, 1.123456m, 3, 2);
        await InsertBalanceForTestAsync(context, insumo.Id, blockedLot.Id, 2.000001m, 3, 2);
        blockedLot.DefinirBloqueado(true);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloadedSupply = await context.InsumosEstoque.SingleAsync(item => item.Id == insumo.Id);
        var lots = await context.LotesInsumo.Where(item => item.InsumoId == insumo.Id).ToArrayAsync();
        var balances = await context.SaldosInsumo.Where(item => item.InsumoId == insumo.Id).ToArrayAsync();
        var referenceDate = new DateOnly(2026, 11, 16);

        Assert.False(lots.Single(item => item.Id == expiredLot.Id).EstaElegivelEm(referenceDate));
        Assert.False(lots.Single(item => item.Id == blockedLot.Id).EstaElegivelEm(referenceDate));
        Assert.Equal(1.123456m, balances.Single(item => item.LoteId == expiredLot.Id).QuantidadeFisica.ValorBase);
        Assert.Equal(2.000001m, balances.Single(item => item.LoteId == blockedLot.Id).QuantidadeFisica.ValorBase);
        Assert.Equal(3.123457m, reloadedSupply.CalcularSaldoFisico(balances).ValorBase);
    }

    [SqlServerIntegrationFact]
    public async Task DatabaseEnforcesBalancePrecisionIdentityAndOwnershipConstraints()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var aggregateMass = InsumoEstoque.Criar(
            "Massa agregada",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        var aggregateCount = InsumoEstoque.Criar(
            "Contagem agregada",
            TipoInsumoEstoque.Embalagem,
            UnidadeFisicaEstoque.Unidade);
        var trackedFirst = InsumoEstoque.Criar(
            "Lotes A",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            controlaLote: true);
        var trackedSecond = InsumoEstoque.Criar(
            "Lotes B",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            controlaLote: true);
        var firstLot = LoteInsumo.Criar(trackedFirst, "MESMO-CODIGO", new DateOnly(2026, 11, 16));
        var secondLot = LoteInsumo.Criar(trackedSecond, "MESMO-CODIGO", new DateOnly(2026, 11, 16));
        context.InsumosEstoque.AddRange(aggregateMass, aggregateCount, trackedFirst, trackedSecond);
        context.LotesInsumo.AddRange(firstLot, secondLot);
        await context.SaveChangesAsync();

        await InsertBalanceForTestAsync(context, aggregateMass.Id, null, 0m, 1, 1);
        await Assert.ThrowsAsync<SqlException>(() =>
            InsertBalanceForTestAsync(context, aggregateMass.Id, null, 1m, 1, 1));
        await Assert.ThrowsAsync<SqlException>(() =>
            InsertBalanceForTestAsync(context, aggregateMass.Id, null, -1m, 1, 1));
        await Assert.ThrowsAsync<SqlException>(() =>
            InsertBalanceForTestAsync(context, aggregateCount.Id, null, 1.5m, 5, 3));
        await Assert.ThrowsAsync<SqlException>(() =>
            InsertBalanceForTestAsync(context, aggregateMass.Id, null, 1m, 2, 1));
        await Assert.ThrowsAsync<SqlException>(() =>
            InsertBalanceForTestAsync(context, Guid.NewGuid(), null, 1m, 1, 1));

        await InsertBalanceForTestAsync(context, trackedFirst.Id, firstLot.Id, 1.123456m, 1, 1);
        await Assert.ThrowsAsync<SqlException>(() =>
            InsertBalanceForTestAsync(context, trackedFirst.Id, firstLot.Id, 1m, 1, 1));
        await Assert.ThrowsAsync<SqlException>(() =>
            InsertBalanceForTestAsync(context, trackedFirst.Id, secondLot.Id, 1m, 1, 1));
        await InsertBalanceForTestAsync(context, trackedSecond.Id, secondLot.Id, 1m, 1, 1);
    }

    private static Task<List<string>> ReadTableNamesAsync(XaquiamuniDbContext context) =>
        context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [schema_id] = SCHEMA_ID(N'dbo')")
            .ToListAsync();

    private static async Task<int> InsertBalanceForTestAsync(
        XaquiamuniDbContext context,
        Guid insumoId,
        Guid? loteId,
        decimal quantidadeBase,
        short unidadeBase,
        short dimensao)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO [SaldosInsumo]
                ([Id], [InsumoId], [LoteId], [Quantidade_ValorBase],
                 [Quantidade_UnidadeBase], [Quantidade_Dimensao])
            VALUES
                (@id, @insumoId, @loteId, @quantidadeBase,
                 @unidadeBase, @dimensao);
            """;
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier)
        {
            Value = Guid.NewGuid()
        });
        command.Parameters.Add(new SqlParameter("@insumoId", SqlDbType.UniqueIdentifier)
        {
            Value = insumoId
        });
        command.Parameters.Add(new SqlParameter("@loteId", SqlDbType.UniqueIdentifier)
        {
            Value = loteId is null ? DBNull.Value : loteId.Value
        });
        command.Parameters.Add(new SqlParameter("@quantidadeBase", SqlDbType.Decimal)
        {
            Precision = QuantidadeEstoque.Precision,
            Scale = QuantidadeEstoque.Scale,
            Value = quantidadeBase
        });
        command.Parameters.Add(new SqlParameter("@unidadeBase", SqlDbType.SmallInt)
        {
            Value = unidadeBase
        });
        command.Parameters.Add(new SqlParameter("@dimensao", SqlDbType.SmallInt)
        {
            Value = dimensao
        });
        return await command.ExecuteNonQueryAsync();
    }
}
