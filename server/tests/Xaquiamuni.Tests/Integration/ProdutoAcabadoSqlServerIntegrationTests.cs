using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class ProdutoAcabadoSqlServerIntegrationTests
{
    private const string MigrationId = "20261117082212_PersistirEstoqueProdutosAcabados";
    private const string PreviousMigrationId =
        "20261116182802_ImplementarSaidasAjustesEEstornosInsumo";

    private static readonly DateTimeOffset RegisteredAtUtc =
        new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [SqlServerIntegrationFact]
    public async Task Migration_CreatesOnlyFinishedGoodsStructuresAndSupportsUpDownUp()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: PreviousMigrationId);
        await using var context = database.CreateVerificationContext();
        var tablesBefore = await ReadTableNamesAsync(context);

        await context.GetService<IMigrator>().MigrateAsync(MigrationId);
        var tablesAfterUp = await ReadTableNamesAsync(context);
        var newTables = tablesAfterUp.Except(tablesBefore, StringComparer.Ordinal).Order().ToArray();

        Assert.Equal(
            [
                "ContagensSemDiferencaProdutoAcabado",
                "LotesProdutoAcabado",
                "MovimentosProdutoAcabado",
                "PoliticasEstoqueProdutoVariante",
                "SaldosProdutoAcabado"
            ],
            newTables);
        Assert.Contains("Produtos", tablesAfterUp);
        Assert.Contains("ProdutoVariantes", tablesAfterUp);
        Assert.Contains("InsumosEstoque", tablesAfterUp);
        Assert.Contains(MigrationId, await context.Database.GetAppliedMigrationsAsync());

        await context.GetService<IMigrator>().MigrateAsync(PreviousMigrationId);
        await using var downContext = database.CreateVerificationContext();
        var tablesAfterDown = await ReadTableNamesAsync(downContext);
        Assert.Equal(tablesBefore.Order(StringComparer.Ordinal), tablesAfterDown.Order(StringComparer.Ordinal));

        await downContext.GetService<IMigrator>().MigrateAsync(MigrationId);
        await using var reappliedContext = database.CreateVerificationContext();
        Assert.Contains(MigrationId, await reappliedContext.Database.GetAppliedMigrationsAsync());
        var reappliedTables = await ReadTableNamesAsync(reappliedContext);
        Assert.All(
            new[]
            {
                "ContagensSemDiferencaProdutoAcabado",
                "LotesProdutoAcabado",
                "MovimentosProdutoAcabado",
                "PoliticasEstoqueProdutoVariante",
                "SaldosProdutoAcabado"
            },
            table => Assert.Contains(table, reappliedTables));
    }

    [SqlServerIntegrationFact]
    public async Task PersistsAndReloadsPolicyLotBalanceMovementAndNoDifferenceIdempotency()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        var politica = PoliticaEstoqueProdutoVariante.Criar(
            variante,
            estoqueMinimo: 3,
            prazoValidadePadraoDias: 30);
        var dataFabricacao = new DateOnly(2026, 3, 8);
        var lote = LoteProdutoAcabado.Criar(variante, politica, dataFabricacao);
        var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
        var movimento = saldo.RegistrarEntrada(
            lote,
            4,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            Guid.NewGuid(),
            "Entrada inicial",
            RegisteredAtUtc,
            "persistencia-entrada",
            Fingerprint());

        context.PoliticasEstoqueProdutoVariante.Add(politica);
        context.LotesProdutoAcabado.Add(lote);
        context.SaldosProdutoAcabado.Add(saldo);
        context.MovimentosProdutoAcabado.Add(movimento);
        await context.SaveChangesAsync();

        var noDifferenceFingerprint = Fingerprint();
        var noDifferenceResponsible = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ContagensSemDiferencaProdutoAcabado]
                ([ResponsavelId], [ChaveIdempotencia], [FingerprintRequisicao], [RegistradoEmUtc])
            VALUES
                ({noDifferenceResponsible}, {"contagem-sem-diferenca"},
                 {noDifferenceFingerprint}, {RegisteredAtUtc});
            """);
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ContagensSemDiferencaProdutoAcabado]
                ([ResponsavelId], [ChaveIdempotencia], [FingerprintRequisicao], [RegistradoEmUtc])
            VALUES
                ({noDifferenceResponsible}, {"contagem-sem-diferenca"},
                 {Fingerprint()}, {RegisteredAtUtc});
            """));

        await using var verificationContext = database.CreateVerificationContext();
        var persistedPolicy = await verificationContext.PoliticasEstoqueProdutoVariante
            .SingleAsync(item => item.ProdutoVarianteId == variante.Id);
        var persistedLot = await verificationContext.LotesProdutoAcabado
            .SingleAsync(item => item.Id == lote.Id);
        var persistedBalance = await verificationContext.SaldosProdutoAcabado
            .SingleAsync(item => item.Id == saldo.Id);
        var persistedMovement = await verificationContext.MovimentosProdutoAcabado
            .SingleAsync(item => item.Id == movimento.Id);
        var persistedNoDifference = await verificationContext.ContagensSemDiferencaProdutoAcabado
            .SingleAsync(item => item.ResponsavelId == noDifferenceResponsible);

        Assert.Equal(3, persistedPolicy.EstoqueMinimo);
        Assert.Equal(30, persistedPolicy.PrazoValidadePadraoDias);
        Assert.Equal(dataFabricacao, persistedLot.DataFabricacao);
        Assert.Equal(dataFabricacao.AddDays(30), persistedLot.DataValidade);
        Assert.Equal(OrigemValidadeLoteProdutoAcabado.PrazoPadrao, persistedLot.OrigemValidade);
        Assert.Equal(30, persistedLot.PrazoValidadeAplicadoDias);
        Assert.Equal(variante.Id, persistedBalance.ProdutoVarianteId);
        Assert.Equal(lote.Id, persistedBalance.LoteId);
        Assert.Equal(4, persistedBalance.QuantidadeVendavel);
        Assert.Equal(4, persistedBalance.QuantidadeFisica);
        Assert.Equal(movimento.Id, persistedMovement.Id);
        Assert.Equal(CondicaoEstoqueProdutoAcabado.Vendavel, persistedMovement.CondicaoDestino);
        Assert.Equal(0, persistedMovement.SaldoAnterior.QuantidadeFisica);
        Assert.Equal(4, persistedMovement.SaldoPosterior.QuantidadeFisica);
        Assert.Equal(RegisteredAtUtc, persistedMovement.RegistradoEmUtc);
        Assert.Equal(Fingerprint(), persistedMovement.FingerprintRequisicao);
        Assert.Equal("contagem-sem-diferenca", persistedNoDifference.ChaveIdempotencia);
        Assert.Equal(noDifferenceFingerprint, persistedNoDifference.FingerprintRequisicao);
    }

    [SqlServerIntegrationFact]
    public async Task DatabaseEnforcesVariantLotUniquenessNonNegativeBalancesAndRestrictedDeletes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        var outraVariante = await CreateVariantAsync(context);
        var politica = PoliticaEstoqueProdutoVariante.Criar(variante, 0, 10);
        var lote = LoteProdutoAcabado.Criar(
            variante,
            politica,
            new DateOnly(2026, 11, 1));
        context.PoliticasEstoqueProdutoVariante.Add(politica);
        context.LotesProdutoAcabado.Add(lote);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [PoliticasEstoqueProdutoVariante]
                ([ProdutoVarianteId], [EstoqueMinimo], [PrazoValidadePadraoDias])
            VALUES
                ({variante.Id}, {1}, {20});
            """));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [PoliticasEstoqueProdutoVariante]
                ([ProdutoVarianteId], [EstoqueMinimo], [PrazoValidadePadraoDias])
            VALUES
                ({Guid.NewGuid()}, {1}, {20});
            """));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [LotesProdutoAcabado]
                ([Id], [ProdutoVarianteId], [Codigo], [DataFabricacao], [DataValidade],
                 [OrigemValidade], [PrazoValidadeAplicadoDias])
            VALUES
                ({Guid.NewGuid()}, {variante.Id}, {lote.Codigo},
                 {lote.DataFabricacao}, {lote.DataValidade}, {(short)lote.OrigemValidade},
                 {lote.PrazoValidadeAplicadoDias});
            """));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [LotesProdutoAcabado]
                ([Id], [ProdutoVarianteId], [Codigo], [DataFabricacao], [DataValidade],
                 [OrigemValidade], [PrazoValidadeAplicadoDias])
            VALUES
                ({Guid.NewGuid()}, {Guid.NewGuid()}, {"LOTE-ORFAO"},
                 {lote.DataFabricacao}, {lote.DataValidade}, {(short)lote.OrigemValidade},
                 {lote.PrazoValidadeAplicadoDias});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [SaldosProdutoAcabado]
                ([Id], [ProdutoVarianteId], [LoteId],
                 [QuantidadeVendavel], [QuantidadeDanificada], [QuantidadeQuarentena])
            VALUES
                ({Guid.NewGuid()}, {variante.Id}, {lote.Id}, {-1}, {0}, {0});
            """));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [SaldosProdutoAcabado]
                ([Id], [ProdutoVarianteId], [LoteId],
                 [QuantidadeVendavel], [QuantidadeDanificada], [QuantidadeQuarentena])
            VALUES
                ({Guid.NewGuid()}, {variante.Id}, {lote.Id}, {int.MaxValue}, {1}, {0});
            """));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [SaldosProdutoAcabado]
                ([Id], [ProdutoVarianteId], [LoteId],
                 [QuantidadeVendavel], [QuantidadeDanificada], [QuantidadeQuarentena])
            VALUES
                ({Guid.NewGuid()}, {outraVariante.Id}, {lote.Id}, {1}, {0}, {0});
            """));

        var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
        context.SaldosProdutoAcabado.Add(saldo);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [LotesProdutoAcabado] WHERE [Id] = {lote.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [ProdutoVariantes] WHERE [Id] = {variante.Id}"));
    }

    [SqlServerIntegrationFact]
    public async Task RowVersionRejectsConcurrentBalanceUpdatesAndRollsBackLosingMovement()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateVerificationContext())
        {
            var variante = await CreateVariantAsync(setupContext);
            var lote = LoteProdutoAcabado.Criar(
                variante,
                politica: null,
                new DateOnly(2026, 11, 1),
                new DateOnly(2027, 1, 1));
            setupContext.LotesProdutoAcabado.Add(lote);
            setupContext.SaldosProdutoAcabado.Add(SaldoLoteProdutoAcabado.CriarZero(lote));
            await setupContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateVerificationContext();
        await using var secondContext = database.CreateVerificationContext();
        var firstSaldo = await firstContext.SaldosProdutoAcabado.SingleAsync();
        var secondSaldo = await secondContext.SaldosProdutoAcabado.SingleAsync();
        var firstLot = await firstContext.LotesProdutoAcabado.SingleAsync();
        var secondLot = await secondContext.LotesProdutoAcabado.SingleAsync();
        var firstMovement = firstSaldo.RegistrarEntrada(
            firstLot,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            Guid.NewGuid(),
            "Primeira atualização",
            RegisteredAtUtc,
            "concorrencia-primeira",
            Fingerprint());
        var secondMovement = secondSaldo.RegistrarEntrada(
            secondLot,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            Guid.NewGuid(),
            "Atualização concorrente",
            RegisteredAtUtc,
            "concorrencia-segunda",
            Fingerprint());
        firstContext.MovimentosProdutoAcabado.Add(firstMovement);
        secondContext.MovimentosProdutoAcabado.Add(secondMovement);

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());

        await using var verificationContext = database.CreateVerificationContext();
        Assert.Equal(1, await verificationContext.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(1, (await verificationContext.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
    }

    [SqlServerIntegrationFact]
    public async Task EstornosRequireMatchingOriginalAndMovementHistoryIsAppendOnly()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CreateVariantAsync(context);
        var firstLot = LoteProdutoAcabado.Criar(
            variante,
            politica: null,
            new DateOnly(2026, 11, 1),
            new DateOnly(2027, 1, 1));
        var secondLot = LoteProdutoAcabado.Criar(
            variante,
            politica: null,
            new DateOnly(2026, 11, 2),
            new DateOnly(2027, 1, 2));
        var firstBalance = SaldoLoteProdutoAcabado.CriarZero(firstLot);
        var secondBalance = SaldoLoteProdutoAcabado.CriarZero(secondLot);
        context.LotesProdutoAcabado.AddRange(firstLot, secondLot);
        context.SaldosProdutoAcabado.AddRange(firstBalance, secondBalance);
        await context.SaveChangesAsync();

        var firstOriginal = firstBalance.RegistrarEntrada(
            firstLot,
            2,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            Guid.NewGuid(),
            "Entrada original",
            RegisteredAtUtc,
            "estorno-original-primeiro",
            Fingerprint());
        var secondOriginal = secondBalance.RegistrarEntrada(
            secondLot,
            2,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            Guid.NewGuid(),
            "Entrada original do segundo lote",
            RegisteredAtUtc,
            "estorno-original-segundo",
            Fingerprint());
        context.MovimentosProdutoAcabado.AddRange(firstOriginal, secondOriginal);
        await context.SaveChangesAsync();

        var reversal = firstBalance.Estornar(
            firstLot,
            firstOriginal,
            [firstOriginal],
            Guid.NewGuid(),
            "Estorno válido",
            RegisteredAtUtc,
            "estorno-valido",
            Fingerprint());
        context.MovimentosProdutoAcabado.Add(reversal);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [MovimentosProdutoAcabado]
                ([Id], [ProdutoVarianteId], [LoteId], [Tipo], [Quantidade],
                 [CondicaoOrigem], [CondicaoDestino], [QuantidadeContada],
                 [SaldoAnterior_QuantidadeVendavel], [SaldoAnterior_QuantidadeDanificada],
                 [SaldoAnterior_QuantidadeQuarentena], [SaldoPosterior_QuantidadeVendavel],
                 [SaldoPosterior_QuantidadeDanificada], [SaldoPosterior_QuantidadeQuarentena],
                 [MovimentoOriginalId], [ResponsavelId], [Motivo], [Observacao],
                 [RegistradoEmUtc], [ChaveIdempotencia], [FingerprintRequisicao])
            VALUES
                ({Guid.NewGuid()}, {variante.Id}, {firstLot.Id}, {(short)TipoMovimentoProdutoAcabado.Estorno},
                 {2}, {(short)CondicaoEstoqueProdutoAcabado.Vendavel}, {null}, {null},
                 {2}, {0}, {0}, {0}, {0}, {0}, {firstOriginal.Id}, {Guid.NewGuid()},
                 {"Estorno duplicado"}, {null}, {RegisteredAtUtc}, {"estorno-duplicado"}, {Fingerprint()});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [MovimentosProdutoAcabado]
                ([Id], [ProdutoVarianteId], [LoteId], [Tipo], [Quantidade],
                 [CondicaoOrigem], [CondicaoDestino], [QuantidadeContada],
                 [SaldoAnterior_QuantidadeVendavel], [SaldoAnterior_QuantidadeDanificada],
                 [SaldoAnterior_QuantidadeQuarentena], [SaldoPosterior_QuantidadeVendavel],
                 [SaldoPosterior_QuantidadeDanificada], [SaldoPosterior_QuantidadeQuarentena],
                 [MovimentoOriginalId], [ResponsavelId], [Motivo], [Observacao],
                 [RegistradoEmUtc], [ChaveIdempotencia], [FingerprintRequisicao])
            VALUES
                ({Guid.NewGuid()}, {variante.Id}, {secondLot.Id}, {(short)TipoMovimentoProdutoAcabado.Estorno},
                 {2}, {(short)CondicaoEstoqueProdutoAcabado.Vendavel}, {null}, {null},
                 {2}, {0}, {0}, {0}, {0}, {0}, {firstOriginal.Id}, {Guid.NewGuid()},
                 {"Estorno de outro lote"}, {null}, {RegisteredAtUtc}, {"estorno-lote-alheio"}, {Fingerprint()});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [MovimentosProdutoAcabado]
            SET [Motivo] = {"Movimento alterado"}
            WHERE [Id] = {firstOriginal.Id};
            """));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [MovimentosProdutoAcabado] WHERE [Id] = {firstOriginal.Id}"));
        Assert.Equal(0, firstBalance.QuantidadeFisica);
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

    private static byte[] Fingerprint() =>
        System.Security.Cryptography.SHA256.HashData("produto-acabado"u8);

    private static async Task<string[]> ReadTableNamesAsync(XaquiamuniDbContext context) =>
        (await context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [schema_id] = SCHEMA_ID(N'dbo')")
            .ToListAsync()).ToArray();
}
