using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class RegistroEntradaInsumoSqlServerIntegrationTests
{
    private const string MigrationId = "20261116182802_ImplementarSaidasAjustesEEstornosInsumo";
    private const string PreviousMigrationId = "20261116173714_RegistrarEntradasInsumos";

    [SqlServerIntegrationFact]
    public async Task Migration_AddsLedgerAndAppendOnlyTriggerAndSupportsUpDownUp()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigrationId);
        Assert.Contains(
            "MovimentosInsumo",
            await context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [name] = N'MovimentosInsumo'")
                .ToListAsync());
        Assert.DoesNotContain(
            "AjustesSemDiferencaInsumo",
            await context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [name] = N'AjustesSemDiferencaInsumo'")
                .ToListAsync());

        await migrator.MigrateAsync(MigrationId);
        Assert.Contains(MigrationId, await context.Database.GetAppliedMigrationsAsync());
        Assert.Contains(
            "MovimentosInsumo",
            await context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [name] = N'MovimentosInsumo'")
                .ToListAsync());
        Assert.Equal(
            1,
            await context.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM [sys].[triggers] WHERE [name] = N'TR_MovimentosInsumo_AppendOnly'")
                .SingleAsync());
        Assert.Equal(
            1,
            await context.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM [sys].[triggers] WHERE [name] = N'TR_MovimentosInsumo_Estorno_Valid'")
                .SingleAsync());
        Assert.Equal(
            1,
            await context.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM [sys].[triggers] WHERE [name] = N'TR_AjustesSemDiferencaInsumo_AppendOnly'")
                .SingleAsync());
        Assert.Contains(
            "AjustesSemDiferencaInsumo",
            await context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [name] = N'AjustesSemDiferencaInsumo'")
                .ToListAsync());

        await migrator.MigrateAsync(PreviousMigrationId);
        Assert.Equal(
            1,
            await context.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM [sys].[triggers] WHERE [name] = N'TR_MovimentosInsumo_AppendOnly'")
                .SingleAsync());
        Assert.Equal(
            0,
            await context.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM [sys].[triggers] WHERE [name] = N'TR_MovimentosInsumo_Estorno_Valid'")
                .SingleAsync());
        Assert.Equal(
            0,
            await context.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM [sys].[triggers] WHERE [name] = N'TR_AjustesSemDiferencaInsumo_AppendOnly'")
                .SingleAsync());
        Assert.DoesNotContain(
            "AjustesSemDiferencaInsumo",
            await context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [name] = N'AjustesSemDiferencaInsumo'")
                .ToListAsync());
        await migrator.MigrateAsync(MigrationId);
        Assert.Contains(MigrationId, await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Entry_ConvertsDirectMassVolumeAndCountAndRecordsBalances()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var mass = InsumoEstoque.Criar("Massa", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var volume = InsumoEstoque.Criar("Essência", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro);
        var count = InsumoEstoque.Criar("Potes", TipoInsumoEstoque.Embalagem, UnidadeFisicaEstoque.Unidade);
        context.InsumosEstoque.AddRange(mass, volume, count);
        await context.SaveChangesAsync();

        var massResult = await RegistrarAsync(database, CriarEntrada(
            mass.Id, TipoMovimentoInsumo.EstoqueInicial, 2m, UnidadeFisicaEstoque.Quilograma, "massa"));
        var volumeResult = await RegistrarAsync(database, CriarEntrada(
            volume.Id, TipoMovimentoInsumo.Aquisicao, 3m, UnidadeFisicaEstoque.Litro, "volume"));
        var countResult = await RegistrarAsync(database, CriarEntrada(
            count.Id, TipoMovimentoInsumo.Aquisicao, 4m, UnidadeFisicaEstoque.Unidade, "contagem"));

        Assert.True(massResult.Sucesso, massResult.Erro);
        Assert.True(volumeResult.Sucesso, volumeResult.Erro);
        Assert.True(countResult.Sucesso, countResult.Erro);
        Assert.Equal(2000m, massResult.Movimento!.QuantidadeBase.ValorBase);
        Assert.Equal(3000m, volumeResult.Movimento!.QuantidadeBase.ValorBase);
        Assert.Equal(4m, countResult.Movimento!.QuantidadeBase.ValorBase);
        Assert.Equal(0m, massResult.Movimento.SaldoAnterior.ValorBase);
        Assert.Equal(2000m, massResult.Movimento.SaldoPosterior.ValorBase);
        Assert.Null(countResult.Movimento.CustoTotal);

        context.ChangeTracker.Clear();
        var duplicateOpening = await RegistrarAsync(database, CriarEntrada(
            mass.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            1m,
            UnidadeFisicaEstoque.Quilograma,
            "duplicate-opening"));
        Assert.Equal(RegistrarEntradaInsumoStatus.EntradaInvalida, duplicateOpening.Status);
        Assert.Equal(2000m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == mass.Id))
            .QuantidadeFisica.ValorBase);
        Assert.Equal(3, await context.MovimentosInsumo.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task EntryByPurchaseForm_PreservesSnapshotCostAndIdempotentResult()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var form = supply.AdicionarFormaCompra("Pacote 500 g", 500m, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();
        var input = CriarEntrada(
            supply.Id,
            TipoMovimentoInsumo.Aquisicao,
            3m,
            unidade: null,
            chave: "  form   entry  ",
            formaCompraId: form.Id,
            custoTotal: 12.50m);

        var first = await RegistrarAsync(database, input);
        Assert.True(first.Sucesso, first.Erro);
        Assert.Equal(1500m, first.Movimento!.QuantidadeBase.ValorBase);
        Assert.Equal(form.Id, first.Movimento.FormaCompraId);
        Assert.Equal("Pacote 500 g", first.Movimento.FormaCompraNome);
        Assert.Equal(500m, first.Movimento.FatorCompra!.ValorBase);
        Assert.Equal(12.50m, first.Movimento.CustoTotal);
        Assert.Equal(0.00833333333333m, first.Movimento.CustoPorUnidadeBase);

        supply.AtualizarFormaCompra(form.Id, "Pacote alterado", 700m, UnidadeFisicaEstoque.Grama);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var replay = await RegistrarAsync(database, input with { ChaveIdempotencia = "form entry" });
        Assert.True(replay.Sucesso, replay.Erro);
        Assert.Equal(first.Movimento.Id, replay.Movimento!.Id);
        Assert.Equal("Pacote 500 g", replay.Movimento.FormaCompraNome);
        Assert.Equal(500m, replay.Movimento.FatorCompra!.ValorBase);
        Assert.Equal(1, await context.MovimentosInsumo.CountAsync());
        Assert.Equal(1500m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id))
            .QuantidadeFisica.ValorBase);

        var otherActorResult = await RegistrarAsync(database, input with { ResponsavelId = Guid.NewGuid() });
        Assert.True(otherActorResult.Sucesso, otherActorResult.Erro);
        Assert.NotEqual(first.Movimento.Id, otherActorResult.Movimento!.Id);

        var conflict = await RegistrarAsync(database, input with { QuantidadeOriginal = 4m });
        Assert.Equal(RegistrarEntradaInsumoStatus.ConflitoIdempotencia, conflict.Status);
        Assert.Equal(2, await context.MovimentosInsumo.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Entry_ReusesOwnedLotAndRejectsForeignLotsAndInactiveInputs()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var tracked = InsumoEstoque.Criar(
            "Óleo",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true,
            validadeObrigatoria: true);
        var foreign = InsumoEstoque.Criar(
            "Outra essência",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true,
            validadeObrigatoria: true);
        var foreignForm = foreign.AdicionarFormaCompra("Frasco de outra essência", 100m, UnidadeFisicaEstoque.Mililitro);
        var inactive = InsumoEstoque.Criar(
            "Inativo",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        var form = tracked.AdicionarFormaCompra("Frasco", 250m, UnidadeFisicaEstoque.Mililitro);
        context.InsumosEstoque.AddRange(tracked, foreign, inactive);
        await context.SaveChangesAsync();
        inactive.DefinirAtivo(false);
        tracked.DefinirFormaCompraAtiva(form.Id, false);
        await context.SaveChangesAsync();

        var created = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            1.123456m,
            UnidadeFisicaEstoque.Litro,
            "lot-create",
            identificacaoLote: "OLEO-A",
            dataEntradaLote: new DateOnly(2026, 11, 16),
            validadeLote: new DateOnly(2099, 12, 31)));
        Assert.True(created.Sucesso, created.Erro);
        var lotId = created.Movimento!.LoteId!.Value;

        var reused = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.Aquisicao,
            2m,
            UnidadeFisicaEstoque.Mililitro,
            "lot-reuse",
            loteId: lotId));
        Assert.True(reused.Sucesso, reused.Erro);
        Assert.Equal(lotId, reused.Movimento!.LoteId);
        Assert.Equal(1125.456m, reused.Movimento.SaldoPosterior.ValorBase);
        Assert.Equal(1, await context.LotesInsumo.CountAsync(item => item.InsumoId == tracked.Id));

        var foreignLot = LoteInsumo.Criar(
            foreign,
            "FOREIGN",
            new DateOnly(2026, 11, 16),
            new DateOnly(2099, 12, 31));
        context.LotesInsumo.Add(foreignLot);
        await context.SaveChangesAsync();
        var wrongLot = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.Aquisicao,
            1m,
            UnidadeFisicaEstoque.Mililitro,
            "wrong-lot",
            loteId: foreignLot.Id));
        Assert.Equal(RegistrarEntradaInsumoStatus.LoteNaoEncontrado, wrongLot.Status);

        var inactiveSupply = await RegistrarAsync(database, CriarEntrada(
            inactive.Id,
            TipoMovimentoInsumo.Aquisicao,
            1m,
            UnidadeFisicaEstoque.Grama,
            "inactive-supply"));
        Assert.Equal(RegistrarEntradaInsumoStatus.InsumoInativo, inactiveSupply.Status);
        var inactiveForm = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.Aquisicao,
            1m,
            unidade: null,
            chave: "inactive-form",
            formaCompraId: form.Id,
            loteId: lotId));
        Assert.Equal(RegistrarEntradaInsumoStatus.EntradaInvalida, inactiveForm.Status);
        var foreignFormResult = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.Aquisicao,
            1m,
            unidade: null,
            chave: "foreign-form",
            formaCompraId: foreignForm.Id,
            loteId: lotId));
        Assert.Equal(RegistrarEntradaInsumoStatus.EntradaInvalida, foreignFormResult.Status);
        Assert.Equal(2, await context.MovimentosInsumo.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Entry_PreservesSixDecimalsAndRejectsQuantityOverflow()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var precise = InsumoEstoque.Criar("Precisão", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var maximum = InsumoEstoque.Criar("Máximo", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.AddRange(precise, maximum);
        await context.SaveChangesAsync();

        var preciseResult = await RegistrarAsync(database, CriarEntrada(
            precise.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            1.123456m,
            UnidadeFisicaEstoque.Grama,
            "six-decimals",
            custoTotal: 0.01m));
        Assert.True(preciseResult.Sucesso, preciseResult.Erro);
        Assert.Equal(1.123456m, preciseResult.Movimento!.QuantidadeBase.ValorBase);
        Assert.Equal(0.01m, preciseResult.Movimento.CustoTotal);
        Assert.Equal(
            decimal.Round(0.01m / 1.123456m, CustoAquisicaoInsumo.CustoUnitarioScale, MidpointRounding.AwayFromZero),
            preciseResult.Movimento.CustoPorUnidadeBase);

        var maxResult = await RegistrarAsync(database, CriarEntrada(
            maximum.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            999999999999.999999m,
            UnidadeFisicaEstoque.Grama,
            "maximum"));
        Assert.True(maxResult.Sucesso, maxResult.Erro);
        var overflow = await RegistrarAsync(database, CriarEntrada(
            maximum.Id,
            TipoMovimentoInsumo.Aquisicao,
            0.000001m,
            UnidadeFisicaEstoque.Grama,
            "overflow"));
        Assert.Equal(RegistrarEntradaInsumoStatus.EntradaInvalida, overflow.Status);
        Assert.Equal(
            999999999999.999999m,
            (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == maximum.Id))
                .QuantidadeFisica.ValorBase);
        Assert.Equal(1, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == maximum.Id));
    }

    [SqlServerIntegrationFact]
    public async Task Entry_InitialStockCanPreserveExpiredBlockedLotButAcquisitionCannot()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var tracked = InsumoEstoque.Criar(
            "Óleo",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true,
            validadeObrigatoria: true);
        context.InsumosEstoque.Add(tracked);
        await context.SaveChangesAsync();

        var initial = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            1m,
            UnidadeFisicaEstoque.Litro,
            "old-lot",
            identificacaoLote: "OLEO-ANTIGO",
            dataEntradaLote: new DateOnly(2020, 1, 1),
            validadeLote: new DateOnly(2020, 1, 2),
            loteBloqueado: true));

        Assert.True(initial.Sucesso, initial.Erro);
        Assert.Equal(1000m, initial.Movimento!.QuantidadeBase.ValorBase);
        Assert.Equal(1000m, initial.Movimento.SaldoPosterior.ValorBase);
        var lot = await context.LotesInsumo.SingleAsync(item => item.InsumoId == tracked.Id);
        Assert.True(lot.Bloqueado);
        Assert.False(lot.EstaElegivelEm(new DateOnly(2026, 11, 16)));

        var acquisition = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.Aquisicao,
            1m,
            UnidadeFisicaEstoque.Mililitro,
            "expired-purchase",
            identificacaoLote: "OLEO-VENCIDO",
            dataEntradaLote: new DateOnly(2020, 1, 1),
            validadeLote: new DateOnly(2020, 1, 2)));
        Assert.Equal(RegistrarEntradaInsumoStatus.LoteInelegivel, acquisition.Status);
        Assert.Equal(1, await context.LotesInsumo.CountAsync());
        Assert.Equal(1, await context.MovimentosInsumo.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Entry_IsAtomicWhenMovementInsertFails()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var tracked = InsumoEstoque.Criar(
            "Cera",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            controlaLote: true);
        context.InsumosEstoque.Add(tracked);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [dbo].[TR_Test_RejectMovementInsert]
            ON [dbo].[MovimentosInsumo]
            AFTER INSERT
            AS
            BEGIN
                THROW 51001, 'Injected movement insert failure.', 1;
            END;
            """);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            1m,
            UnidadeFisicaEstoque.Grama,
            "rollback",
            identificacaoLote: "CERA-1",
            dataEntradaLote: new DateOnly(2026, 11, 16))));
        Assert.IsType<SqlException>(exception.InnerException);

        context.ChangeTracker.Clear();
        Assert.Empty(await context.LotesInsumo.Where(item => item.InsumoId == tracked.Id).ToListAsync());
        Assert.Empty(await context.SaldosInsumo.Where(item => item.InsumoId == tracked.Id).ToListAsync());
        Assert.Empty(await context.MovimentosInsumo.Where(item => item.InsumoId == tracked.Id).ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task MovementLedger_RejectsDatabaseUpdatesAndDeletes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();
        var result = await RegistrarAsync(database, CriarEntrada(
            supply.Id, TipoMovimentoInsumo.EstoqueInicial, 1m, UnidadeFisicaEstoque.Grama, "append-only"));
        Assert.True(result.Sucesso, result.Erro);

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [MovimentosInsumo] SET [Observacao] = {"alteração"} WHERE [Id] = {result.Movimento!.Id};
            """));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [MovimentosInsumo] WHERE [Id] = {result.Movimento!.Id};
            """));
        Assert.Equal(1, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == supply.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentEntriesAndRetries_DoNotLoseOrDuplicateStock()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Pigmento", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();

        var retryInput = CriarEntrada(
            supply.Id, TipoMovimentoInsumo.Aquisicao, 1m, UnidadeFisicaEstoque.Grama, "same-key");
        var duplicateResults = await Task.WhenAll(
            RegistrarAsync(database, retryInput),
            RegistrarAsync(database, retryInput));
        Assert.All(duplicateResults, result => Assert.True(result.Sucesso, result.Erro));
        Assert.Equal(duplicateResults[0].Movimento!.Id, duplicateResults[1].Movimento!.Id);

        var distinctResults = await Task.WhenAll(
            RegistrarAsync(database, CriarEntrada(
                supply.Id, TipoMovimentoInsumo.Aquisicao, 2m, UnidadeFisicaEstoque.Grama, "different-key-a")),
            RegistrarAsync(database, CriarEntrada(
                supply.Id, TipoMovimentoInsumo.Aquisicao, 3m, UnidadeFisicaEstoque.Grama, "different-key-b")));
        Assert.All(distinctResults, result => Assert.True(result.Sucesso, result.Erro));
        Assert.Equal(6m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id))
            .QuantidadeFisica.ValorBase);
        Assert.Equal(3, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == supply.Id));
    }

    [SqlServerIntegrationFact]
    public async Task LotPolicyChangesAreRejectedAfterAnyStockHistory()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();

        var policy = await AtualizarPoliticaAsync(database, supply.Id, controlaLote: true, validadeObrigatoria: true);
        Assert.Equal(AtualizarPoliticaLoteInsumoStatus.Atualizado, policy);

        context.ChangeTracker.Clear();
        var updatedSupply = await context.InsumosEstoque.SingleAsync(item => item.Id == supply.Id);
        Assert.Throws<InvalidOperationException>(() => updatedSupply.AtualizarDados(
            "Cera",
            TipoInsumoEstoque.MateriaPrima,
            null,
            controlaLote: false,
            validadeObrigatoria: false));

        var entry = await RegistrarAsync(database, CriarEntrada(
            supply.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            1m,
            UnidadeFisicaEstoque.Grama,
            "policy-history",
            identificacaoLote: "CERA-1",
            dataEntradaLote: new DateOnly(2026, 11, 16),
            validadeLote: new DateOnly(2027, 1, 1)));
        Assert.True(entry.Sucesso, entry.Erro);

        var rejected = await AtualizarPoliticaAsync(database, supply.Id, controlaLote: false, validadeObrigatoria: false);
        Assert.Equal(AtualizarPoliticaLoteInsumoStatus.HistoricoExistente, rejected);
        context.ChangeTracker.Clear();
        Assert.True((await context.InsumosEstoque.SingleAsync(item => item.Id == supply.Id)).ControlaLote);
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentEntryAndLotPolicyChangeCannotCreateAnInconsistentPosition()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();

        var entryTask = RegistrarAsync(database, CriarEntrada(
            supply.Id, TipoMovimentoInsumo.EstoqueInicial, 1m, UnidadeFisicaEstoque.Grama, "policy-race"));
        var policyTask = AtualizarPoliticaAsync(
            database, supply.Id, controlaLote: true, validadeObrigatoria: true);
        await Task.WhenAll(entryTask, policyTask);

        var entry = await entryTask;
        var policy = await policyTask;
        context.ChangeTracker.Clear();
        var storedSupply = await context.InsumosEstoque.SingleAsync(item => item.Id == supply.Id);
        var movements = await context.MovimentosInsumo.Where(item => item.InsumoId == supply.Id).ToArrayAsync();
        var balances = await context.SaldosInsumo.Where(item => item.InsumoId == supply.Id).ToArrayAsync();

        if (entry.Sucesso)
        {
            Assert.Equal(AtualizarPoliticaLoteInsumoStatus.HistoricoExistente, policy);
            Assert.False(storedSupply.ControlaLote);
            Assert.Single(movements);
            Assert.Null(Assert.Single(balances).LoteId);
        }
        else
        {
            Assert.Equal(RegistrarEntradaInsumoStatus.EntradaInvalida, entry.Status);
            Assert.Equal(AtualizarPoliticaLoteInsumoStatus.Atualizado, policy);
            Assert.True(storedSupply.ControlaLote);
            Assert.Empty(movements);
            Assert.Empty(balances);
        }
    }

    [SqlServerIntegrationFact]
    public async Task ManualExitIsAtomicIdempotentAndCannotMakeBalanceNegative()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();
        Assert.True((await RegistrarAsync(database, CriarEntrada(
            supply.Id, TipoMovimentoInsumo.EstoqueInicial, 10m, UnidadeFisicaEstoque.Grama, "exit-opening")))
            .Sucesso);

        var input = CriarSaida(
            supply.Id,
            TipoMovimentoInsumo.SaidaManual,
            3m,
            UnidadeFisicaEstoque.Grama,
            "exit-manual",
            "Consumo operacional autorizado");
        var first = await RegistrarSaidaAsync(database, input);
        var replay = await RegistrarSaidaAsync(database, input);
        Assert.True(first.Sucesso, first.Erro);
        Assert.True(replay.Sucesso, replay.Erro);
        Assert.Equal(first.Movimento!.Id, replay.Movimento!.Id);
        Assert.Equal(DirecaoMovimentoInsumo.Saida, first.Movimento.Direcao);
        Assert.Equal(10m, first.Movimento.SaldoAnterior.ValorBase);
        Assert.Equal(7m, first.Movimento.SaldoPosterior.ValorBase);

        var insufficient = await RegistrarSaidaAsync(database, CriarSaida(
            supply.Id,
            TipoMovimentoInsumo.SaidaManual,
            8m,
            UnidadeFisicaEstoque.Grama,
            "exit-insufficient",
            "Saída maior que saldo"));
        Assert.Equal(OperacaoEstoqueInsumoStatus.SaldoInsuficiente, insufficient.Status);
        Assert.Equal(7m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id))
            .QuantidadeFisica.ValorBase);
        Assert.Equal(2, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == supply.Id));
    }

    [SqlServerIntegrationFact]
    public async Task LossAndDisposalCanRemoveExpiredBlockedLotButOperationalExitCannot()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar(
            "Óleo",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();
        var opening = await RegistrarAsync(database, CriarEntrada(
            supply.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            3m,
            UnidadeFisicaEstoque.Mililitro,
            "blocked-lot-opening",
            identificacaoLote: "OLEO-VENCIDO",
            dataEntradaLote: new DateOnly(2020, 1, 1),
            validadeLote: new DateOnly(2020, 1, 2),
            loteBloqueado: true));
        Assert.True(opening.Sucesso, opening.Erro);
        var lotId = opening.Movimento!.LoteId!.Value;

        var operational = await RegistrarSaidaAsync(database, CriarSaida(
            supply.Id,
            TipoMovimentoInsumo.SaidaManual,
            1m,
            UnidadeFisicaEstoque.Mililitro,
            "blocked-operational",
            "Consumo",
            lotId));
        Assert.Equal(OperacaoEstoqueInsumoStatus.LoteInelegivel, operational.Status);

        var loss = await RegistrarSaidaAsync(database, CriarSaida(
            supply.Id,
            TipoMovimentoInsumo.Perda,
            1m,
            UnidadeFisicaEstoque.Mililitro,
            "expired-loss",
            "Perda por vencimento",
            lotId));
        var disposal = await RegistrarSaidaAsync(database, CriarSaida(
            supply.Id,
            TipoMovimentoInsumo.Descarte,
            1m,
            UnidadeFisicaEstoque.Mililitro,
            "blocked-disposal",
            "Descarte autorizado",
            lotId));

        Assert.True(loss.Sucesso, loss.Erro);
        Assert.True(disposal.Sucesso, disposal.Erro);
        Assert.Equal(TipoMovimentoInsumo.Perda, loss.Movimento!.Tipo);
        Assert.Equal(TipoMovimentoInsumo.Descarte, disposal.Movimento!.Tipo);
        Assert.Equal(lotId, loss.Movimento.LoteId);
        Assert.Equal(1m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id))
            .QuantidadeFisica.ValorBase);
        Assert.Equal(3, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == supply.Id));
    }

    [SqlServerIntegrationFact]
    public async Task InventoryCountAppliesSignedDifferenceAndStoresNoDifferenceIdempotently()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Pigmento", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();
        var actor = Guid.NewGuid();

        var firstCount = CriarContagem(
            supply.Id, 5m, UnidadeFisicaEstoque.Grama, null, null, "count-positive", actor);
        var increase = await AjustarAsync(database, firstCount);
        Assert.True(increase.Sucesso, increase.Erro);
        Assert.Equal(TipoMovimentoInsumo.AjusteInventario, increase.Movimento!.Tipo);
        Assert.Equal(DirecaoMovimentoInsumo.Entrada, increase.Movimento.Direcao);
        Assert.Equal(5m, increase.Movimento.QuantidadeBase.ValorBase);
        Assert.Equal(5m, increase.Movimento.QuantidadeContada!.ValorBase);
        Assert.Equal(increase.Movimento.Id, (await AjustarAsync(database, firstCount)).Movimento!.Id);

        context.ChangeTracker.Clear();
        var balance = await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id);
        var version = context.Entry(balance).Property<byte[]>("Versao").CurrentValue!;
        var lowerCount = CriarContagem(
            supply.Id, 3m, UnidadeFisicaEstoque.Grama, null, version, "count-negative", actor);
        var decrease = await AjustarAsync(database, lowerCount);
        Assert.True(decrease.Sucesso, decrease.Erro);
        Assert.Equal(DirecaoMovimentoInsumo.Saida, decrease.Movimento!.Direcao);
        Assert.Equal(2m, decrease.Movimento.QuantidadeBase.ValorBase);
        Assert.Equal(3m, decrease.Movimento.SaldoPosterior.ValorBase);
        Assert.Equal(3m, decrease.Movimento.QuantidadeContada!.ValorBase);

        context.ChangeTracker.Clear();
        balance = await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id);
        version = context.Entry(balance).Property<byte[]>("Versao").CurrentValue!;
        var unchangedCount = CriarContagem(
            supply.Id, 3m, UnidadeFisicaEstoque.Grama, null, version, "count-no-difference", actor);
        var noDifference = await AjustarAsync(database, unchangedCount);
        Assert.Equal(OperacaoEstoqueInsumoStatus.SemDiferenca, noDifference.Status);
        Assert.Null(noDifference.Movimento);
        Assert.Equal(2, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == supply.Id));
        Assert.Equal(1, await context.AjustesSemDiferencaInsumo.CountAsync());
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [AjustesSemDiferencaInsumo]
            WHERE [ResponsavelId] = {actor} AND [ChaveIdempotencia] = {"count-no-difference"};
            """));

        var replay = await AjustarAsync(database, unchangedCount);
        Assert.Equal(OperacaoEstoqueInsumoStatus.SemDiferenca, replay.Status);
        var changedPayload = await AjustarAsync(database, unchangedCount with { QuantidadeEncontrada = 2m });
        Assert.Equal(OperacaoEstoqueInsumoStatus.ConflitoIdempotencia, changedPayload.Status);

        var crossOperationKey = await RegistrarAsync(database, CriarEntrada(
            supply.Id,
            TipoMovimentoInsumo.Aquisicao,
            1m,
            UnidadeFisicaEstoque.Grama,
            "count-no-difference") with { ResponsavelId = actor });
        Assert.Equal(RegistrarEntradaInsumoStatus.ConflitoIdempotencia, crossOperationKey.Status);
    }

    [SqlServerIntegrationFact]
    public async Task InventoryCountRequiresExactLotAndRejectsStaleRowVersion()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var tracked = InsumoEstoque.Criar(
            "Essência",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true);
        var aggregate = InsumoEstoque.Criar(
            "Cera",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.AddRange(tracked, aggregate);
        await context.SaveChangesAsync();
        var opening = await RegistrarAsync(database, CriarEntrada(
            tracked.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            5m,
            UnidadeFisicaEstoque.Mililitro,
            "count-lot-opening",
            identificacaoLote: "ESSENCIA-1",
            dataEntradaLote: new DateOnly(2026, 11, 16)));
        Assert.True(opening.Sucesso, opening.Erro);
        var lotId = opening.Movimento!.LoteId!.Value;
        var lotBalance = await context.SaldosInsumo.SingleAsync(item => item.InsumoId == tracked.Id);
        var lotVersion = context.Entry(lotBalance).Property<byte[]>("Versao").CurrentValue!;

        var missingLot = await AjustarAsync(database, CriarContagem(
            tracked.Id, 4m, UnidadeFisicaEstoque.Mililitro, null, lotVersion, "count-lot-missing"));
        Assert.Equal(OperacaoEstoqueInsumoStatus.OperacaoInvalida, missingLot.Status);
        var lotCount = await AjustarAsync(database, CriarContagem(
            tracked.Id, 4m, UnidadeFisicaEstoque.Mililitro, lotId, lotVersion, "count-lot-specific"));
        Assert.True(lotCount.Sucesso, lotCount.Erro);
        Assert.Equal(lotId, lotCount.Movimento!.LoteId);
        Assert.Equal(4m, lotCount.Movimento.SaldoPosterior.ValorBase);

        var aggregateOpening = await RegistrarAsync(database, CriarEntrada(
            aggregate.Id, TipoMovimentoInsumo.EstoqueInicial, 5m, UnidadeFisicaEstoque.Grama, "count-aggregate-opening"));
        Assert.True(aggregateOpening.Sucesso, aggregateOpening.Erro);
        context.ChangeTracker.Clear();
        var aggregateBalance = await context.SaldosInsumo.SingleAsync(item => item.InsumoId == aggregate.Id);
        var staleVersion = context.Entry(aggregateBalance).Property<byte[]>("Versao").CurrentValue!;
        Assert.True((await RegistrarSaidaAsync(database, CriarSaida(
            aggregate.Id, TipoMovimentoInsumo.SaidaManual, 1m, UnidadeFisicaEstoque.Grama,
            "count-concurrent-exit", "Saída concorrente"))).Sucesso);

        var staleCount = await AjustarAsync(database, CriarContagem(
            aggregate.Id, 4m, UnidadeFisicaEstoque.Grama, null, staleVersion, "count-stale"));
        Assert.Equal(OperacaoEstoqueInsumoStatus.ConflitoConcorrencia, staleCount.Status);
        context.ChangeTracker.Clear();
        Assert.Equal(4m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == aggregate.Id))
            .QuantidadeFisica.ValorBase);
        Assert.Equal(2, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == aggregate.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ReversalLinksOriginalPreservesCostAndRejectsDuplicateOrInsufficientCompensation()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var impossibleSupply = InsumoEstoque.Criar(
            "Pigmento",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.AddRange(supply, impossibleSupply);
        await context.SaveChangesAsync();

        var original = await RegistrarAsync(database, CriarEntrada(
            supply.Id,
            TipoMovimentoInsumo.Aquisicao,
            5m,
            UnidadeFisicaEstoque.Grama,
            "reversal-original",
            custoTotal: 7.25m));
        Assert.True(original.Sucesso, original.Erro);
        var reversalInput = new EstornarMovimentoInsumoInput(
            original.Movimento!.Id,
            Guid.NewGuid(),
            "Correção da entrada",
            "reversal-valid");
        var reversal = await EstornarAsync(database, reversalInput);
        Assert.True(reversal.Sucesso, reversal.Erro);
        Assert.Equal(TipoMovimentoInsumo.Estorno, reversal.Movimento!.Tipo);
        Assert.Equal(original.Movimento.Id, reversal.Movimento.MovimentoOriginalId);
        Assert.Equal(DirecaoMovimentoInsumo.Saida, reversal.Movimento.Direcao);
        Assert.Null(reversal.Movimento.CustoTotal);
        Assert.Null(reversal.Movimento.CustoPorUnidadeBase);
        Assert.Null(reversal.Movimento.FormaCompraId);
        Assert.Null(reversal.Movimento.FatorCompra);
        Assert.Equal(7.25m, (await context.MovimentosInsumo
            .SingleAsync(item => item.Id == original.Movimento.Id)).CustoTotal);
        Assert.Equal(0m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id))
            .QuantidadeFisica.ValorBase);

        var duplicate = await EstornarAsync(database, reversalInput with
        {
            ChaveIdempotencia = "reversal-second-key"
        });
        Assert.Equal(OperacaoEstoqueInsumoStatus.MovimentoJaEstornado, duplicate.Status);
        var replay = await EstornarAsync(database, reversalInput);
        Assert.Equal(reversal.Movimento.Id, replay.Movimento!.Id);
        Assert.Equal(2, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == supply.Id));

        var impossibleEntry = await RegistrarAsync(database, CriarEntrada(
            impossibleSupply.Id,
            TipoMovimentoInsumo.EstoqueInicial,
            5m,
            UnidadeFisicaEstoque.Grama,
            "reversal-insufficient-opening"));
        Assert.True(impossibleEntry.Sucesso, impossibleEntry.Erro);
        Assert.True((await RegistrarSaidaAsync(database, CriarSaida(
            impossibleSupply.Id,
            TipoMovimentoInsumo.SaidaManual,
            4m,
            UnidadeFisicaEstoque.Grama,
            "reversal-insufficient-exit",
            "Consumo"))).Sucesso);
        var insufficient = await EstornarAsync(database, new EstornarMovimentoInsumoInput(
            impossibleEntry.Movimento!.Id,
            Guid.NewGuid(),
            "Estorno não disponível",
            "reversal-insufficient"));
        Assert.Equal(OperacaoEstoqueInsumoStatus.SaldoInsuficiente, insufficient.Status);
        Assert.Equal(1m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == impossibleSupply.Id))
            .QuantidadeFisica.ValorBase);
        Assert.Equal(2, await context.MovimentosInsumo.CountAsync(item => item.InsumoId == impossibleSupply.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentExitsAndPhysicalCountsAreSerializedAndIdempotent()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();
        Assert.True((await RegistrarAsync(database, CriarEntrada(
            supply.Id, TipoMovimentoInsumo.EstoqueInicial, 10m, UnidadeFisicaEstoque.Grama, "concurrent-opening")))
            .Sucesso);

        var sameExit = CriarSaida(
            supply.Id,
            TipoMovimentoInsumo.SaidaManual,
            2m,
            UnidadeFisicaEstoque.Grama,
            "concurrent-exit-same",
            "Consumo");
        var retries = await Task.WhenAll(
            RegistrarSaidaAsync(database, sameExit),
            RegistrarSaidaAsync(database, sameExit));
        Assert.All(retries, result => Assert.True(result.Sucesso, result.Erro));
        Assert.Equal(retries[0].Movimento!.Id, retries[1].Movimento!.Id);

        context.ChangeTracker.Clear();
        var balance = await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id);
        var version = context.Entry(balance).Property<byte[]>("Versao").CurrentValue!;
        var counts = await Task.WhenAll(
            AjustarAsync(database, CriarContagem(
                supply.Id, 7m, UnidadeFisicaEstoque.Grama, null, version, "concurrent-count-a")),
            AjustarAsync(database, CriarContagem(
                supply.Id, 6m, UnidadeFisicaEstoque.Grama, null, version, "concurrent-count-b")));
        Assert.Single(counts, result => result.Status == OperacaoEstoqueInsumoStatus.Sucesso);
        Assert.Single(counts, result => result.Status == OperacaoEstoqueInsumoStatus.ConflitoConcorrencia);
        Assert.Equal(1, await context.MovimentosInsumo.CountAsync(item =>
            item.Tipo == TipoMovimentoInsumo.AjusteInventario));
    }

    [SqlServerIntegrationFact]
    public async Task FailedOutboundLedgerInsertRollsBackBalanceChange()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var supply = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(supply);
        await context.SaveChangesAsync();
        Assert.True((await RegistrarAsync(database, CriarEntrada(
            supply.Id, TipoMovimentoInsumo.EstoqueInicial, 10m, UnidadeFisicaEstoque.Grama, "outbound-rollback-opening")))
            .Sucesso);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [dbo].[TR_Test_RejectOutboundMovement]
            ON [dbo].[MovimentosInsumo]
            AFTER INSERT
            AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE [Tipo] = 3)
                    THROW 51007, 'Injected outbound movement failure.', 1;
            END;
            """);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => RegistrarSaidaAsync(database, CriarSaida(
            supply.Id,
            TipoMovimentoInsumo.SaidaManual,
            3m,
            UnidadeFisicaEstoque.Grama,
            "outbound-rollback",
            "Teste de rollback")));
        Assert.IsType<SqlException>(exception.InnerException);
        context.ChangeTracker.Clear();
        Assert.Equal(10m, (await context.SaldosInsumo.SingleAsync(item => item.InsumoId == supply.Id))
            .QuantidadeFisica.ValorBase);
        Assert.Single(await context.MovimentosInsumo.Where(item => item.InsumoId == supply.Id).ToListAsync());
    }

    private static async Task<RegistrarEntradaInsumoResult> RegistrarAsync(
        SqlServerTestDatabase database,
        RegistrarEntradaInsumoInput input)
    {
        await using var scope = database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRegistrarEntradaInsumo>()
            .ExecuteAsync(input);
    }

    private static async Task<OperacaoEstoqueInsumoResult> RegistrarSaidaAsync(
        SqlServerTestDatabase database,
        RegistrarSaidaInsumoInput input)
    {
        await using var scope = database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRegistrarSaidaInsumo>()
            .ExecutarAsync(input);
    }

    private static async Task<OperacaoEstoqueInsumoResult> AjustarAsync(
        SqlServerTestDatabase database,
        AjustarContagemInsumoInput input)
    {
        await using var scope = database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAjustarContagemInsumo>()
            .ExecutarAsync(input);
    }

    private static async Task<OperacaoEstoqueInsumoResult> EstornarAsync(
        SqlServerTestDatabase database,
        EstornarMovimentoInsumoInput input)
    {
        await using var scope = database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEstornarMovimentoInsumo>()
            .ExecutarAsync(input);
    }

    private static async Task<AtualizarPoliticaLoteInsumoStatus> AtualizarPoliticaAsync(
        SqlServerTestDatabase database,
        Guid insumoId,
        bool controlaLote,
        bool validadeObrigatoria)
    {
        await using var scope = database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAtualizarPoliticaLoteInsumo>()
            .ExecuteAsync(insumoId, controlaLote, validadeObrigatoria);
    }

    private static RegistrarEntradaInsumoInput CriarEntrada(
        Guid insumoId,
        TipoMovimentoInsumo tipo,
        decimal quantidade,
        UnidadeFisicaEstoque? unidade,
        string chave,
        Guid? formaCompraId = null,
        Guid? loteId = null,
        string? identificacaoLote = null,
        DateOnly? dataEntradaLote = null,
        DateOnly? validadeLote = null,
        bool loteBloqueado = false,
        decimal? custoTotal = null) =>
        new(
            insumoId,
            Guid.NewGuid(),
            tipo,
            quantidade,
            unidade,
            formaCompraId,
            loteId,
            identificacaoLote,
            dataEntradaLote,
            validadeLote,
            null,
            loteBloqueado,
            custoTotal,
            chave);

    private static RegistrarSaidaInsumoInput CriarSaida(
        Guid insumoId,
        TipoMovimentoInsumo tipo,
        decimal quantidade,
        UnidadeFisicaEstoque unidade,
        string chave,
        string motivo,
        Guid? loteId = null) =>
        new(
            insumoId,
            Guid.NewGuid(),
            tipo,
            quantidade,
            unidade,
            loteId,
            motivo,
            chave);

    private static AjustarContagemInsumoInput CriarContagem(
        Guid insumoId,
        decimal quantidadeEncontrada,
        UnidadeFisicaEstoque unidade,
        Guid? loteId,
        byte[]? versaoSaldoEsperada,
        string chave,
        Guid? responsavelId = null) =>
        new(
            insumoId,
            responsavelId ?? Guid.NewGuid(),
            quantidadeEncontrada,
            unidade,
            loteId,
            versaoSaldoEsperada,
            "Contagem física",
            chave);
}
