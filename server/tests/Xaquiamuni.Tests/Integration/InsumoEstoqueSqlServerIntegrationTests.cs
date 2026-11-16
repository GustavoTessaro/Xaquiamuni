using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class InsumoEstoqueSqlServerIntegrationTests
{
    private const string MigrationId = "20261116165809_PersistirInsumosEFormasCompra";
    private const string PreviousMigrationId = "20261115184700_AddProdutoImagens";

    [SqlServerIntegrationFact]
    public async Task Migration_AddsOnlyIngredientAndPurchaseFormTablesAndCanBeReversed()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        var tables = await context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [schema_id] = SCHEMA_ID(N'dbo')")
            .ToListAsync();

        Assert.Contains(MigrationId, appliedMigrations);
        Assert.Contains("AspNetUsers", tables);
        Assert.Contains("Produtos", tables);
        Assert.Contains("InsumosEstoque", tables);
        Assert.Contains("FormasCompraInsumo", tables);
        Assert.DoesNotContain(tables, name =>
            name.Contains("Movimento", StringComparison.Ordinal)
            || name.Contains("Reserva", StringComparison.Ordinal)
            || name.Contains("Custo", StringComparison.Ordinal));

        await context.GetService<IMigrator>().MigrateAsync(MigrationId);
        await using var f421Context = database.CreateVerificationContext();
        var tablesAtF421 = await f421Context.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [schema_id] = SCHEMA_ID(N'dbo')")
            .ToListAsync();
        Assert.Contains("InsumosEstoque", tablesAtF421);
        Assert.Contains("FormasCompraInsumo", tablesAtF421);
        Assert.DoesNotContain("LotesInsumo", tablesAtF421);
        Assert.DoesNotContain("SaldosInsumo", tablesAtF421);

        await f421Context.GetService<IMigrator>().MigrateAsync(PreviousMigrationId);
        await using var downContext = database.CreateVerificationContext();
        var tablesAfterDown = await downContext.Database.SqlQuery<string>(
                $"SELECT [name] AS [Value] FROM [sys].[tables] WHERE [schema_id] = SCHEMA_ID(N'dbo')")
            .ToListAsync();
        Assert.DoesNotContain("InsumosEstoque", tablesAfterDown);
        Assert.DoesNotContain("FormasCompraInsumo", tablesAfterDown);

        await downContext.GetService<IMigrator>().MigrateAsync(MigrationId);
        await using var upContext = database.CreateVerificationContext();
        Assert.Contains(MigrationId, await upContext.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerIntegrationFact]
    public async Task PersistsAndReloadsBaseUnitsMinimumsPurchaseFormsAndUpdates()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();

        var mass = InsumoEstoque.Criar(
            "Base glicerinada",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            QuantidadeEstoque.Criar(0.000001m, UnidadeFisicaEstoque.Grama),
            controlaLote: true,
            validadeObrigatoria: true);
        var massSmallPack = mass.AdicionarFormaCompra("Pacote de 500 g", 500.123456m, UnidadeFisicaEstoque.Grama);
        var massLargePack = mass.AdicionarFormaCompra("Pacote de 1 kg", 1m, UnidadeFisicaEstoque.Quilograma);
        var volume = InsumoEstoque.Criar(
            "Essência",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro);
        var volumeBottle = volume.AdicionarFormaCompra("Frasco de 1 L", 1m, UnidadeFisicaEstoque.Litro);
        var count = InsumoEstoque.Criar(
            "Potes",
            TipoInsumoEstoque.Embalagem,
            UnidadeFisicaEstoque.Unidade,
            QuantidadeEstoque.Criar(0m, UnidadeFisicaEstoque.Unidade));
        var countBox = count.AdicionarFormaCompra("Caixa com 12", 12m, UnidadeFisicaEstoque.Unidade);

        context.InsumosEstoque.AddRange(mass, volume, count);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloaded = await context.InsumosEstoque
            .Include(item => item.FormasCompra)
            .OrderBy(item => item.Nome)
            .ToArrayAsync();
        var persistedMass = reloaded.Single(item => item.Id == mass.Id);
        var persistedVolume = reloaded.Single(item => item.Id == volume.Id);
        var persistedCount = reloaded.Single(item => item.Id == count.Id);

        Assert.Equal(UnidadeFisicaEstoque.Grama, persistedMass.UnidadeBase);
        Assert.Equal(0.000001m, persistedMass.EstoqueMinimo?.ValorBase);
        Assert.Equal(TipoInsumoEstoque.MateriaPrima, persistedMass.Tipo);
        Assert.True(persistedMass.ControlaLote);
        Assert.True(persistedMass.ValidadeObrigatoria);
        Assert.Null(persistedVolume.EstoqueMinimo);
        Assert.Equal(UnidadeFisicaEstoque.Mililitro, persistedVolume.UnidadeBase);
        Assert.Equal(0m, persistedCount.EstoqueMinimo?.ValorBase);
        Assert.Equal(UnidadeFisicaEstoque.Unidade, persistedCount.UnidadeBase);
        Assert.Equal(2, persistedMass.FormasCompra.Count);
        Assert.Equal(500.123456m, persistedMass.FormasCompra.Single(item => item.Id == massSmallPack.Id)
            .QuantidadeEquivalentePorEmbalagem.ValorBase);
        Assert.Equal(
            1500.370368m,
            persistedMass.ConverterCompra(massSmallPack.Id, 3m).QuantidadeBase.ValorBase);
        Assert.Equal(
            1000m,
            persistedVolume.ConverterCompra(volumeBottle.Id, 1m).QuantidadeBase.ValorBase);
        Assert.Equal(
            24m,
            persistedCount.ConverterCompra(countBox.Id, 2m).QuantidadeBase.ValorBase);
        Assert.Equal(massLargePack.Id, persistedMass.FormasCompra.Single(item => item.Id == massLargePack.Id).Id);

        persistedMass.AtualizarDados(
            "Base vegetal",
            TipoInsumoEstoque.MateriaPrima,
            QuantidadeEstoque.Criar(2m, UnidadeFisicaEstoque.Grama),
            controlaLote: false,
            validadeObrigatoria: false);
        persistedMass.DefinirAtivo(false);
        persistedMass.DefinirFormaCompraAtiva(massSmallPack.Id, false);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var updated = await context.InsumosEstoque
            .Include(item => item.FormasCompra)
            .SingleAsync(item => item.Id == mass.Id);
        Assert.Equal("Base vegetal", updated.Nome);
        Assert.Equal(2m, updated.EstoqueMinimo?.ValorBase);
        Assert.False(updated.Ativo);
        Assert.False(updated.ControlaLote);
        Assert.False(updated.ValidadeObrigatoria);
        Assert.False(updated.FormasCompra.Single(item => item.Id == massSmallPack.Id).Ativa);
        Assert.True(updated.FormasCompra.Single(item => item.Id == massLargePack.Id).Ativa);
    }

    [SqlServerIntegrationFact]
    public async Task DatabaseEnforcesScopedNamesForeignKeysAndPhysicalConstraints()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var first = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var second = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        first.AdicionarFormaCompra("Pacote", 500m, UnidadeFisicaEstoque.Grama);
        second.AdicionarFormaCompra("Pacote", 1m, UnidadeFisicaEstoque.Quilograma);
        context.InsumosEstoque.AddRange(first, second);
        await context.SaveChangesAsync();
        first.DefinirFormaCompraAtiva(first.FormasCompra.Single().Id, false);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [FormasCompraInsumo]
                ([Id], [InsumoId], [Nome], [NomeNormalizado], [Fator_ValorBase],
                 [Fator_UnidadeBase], [Fator_Dimensao], [Ativa])
            VALUES
                ({Guid.NewGuid()}, {first.Id}, {"Pacote duplicado"}, {"PACOTE"},
                 {1m}, {1}, {1}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [FormasCompraInsumo]
                ([Id], [InsumoId], [Nome], [NomeNormalizado], [Fator_ValorBase],
                 [Fator_UnidadeBase], [Fator_Dimensao], [Ativa])
            VALUES
                ({Guid.NewGuid()}, {Guid.NewGuid()}, {"Órfã"}, {"ORFA"},
                 {1m}, {1}, {1}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [InsumosEstoque]
                ([Id], [Nome], [NomeNormalizado], [Tipo], [UnidadeBase],
                 [ControlaLote], [ValidadeObrigatoria], [Ativo])
            VALUES
                ({Guid.NewGuid()}, {"Sem validade"}, {"SEM VALIDADE"}, {1}, {1},
                 {false}, {true}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [InsumosEstoque]
                ([Id], [Nome], [NomeNormalizado], [Tipo], [UnidadeBase],
                 [ControlaLote], [ValidadeObrigatoria], [Ativo])
            VALUES
                ({Guid.NewGuid()}, {"Unidade não canônica"}, {"UNIDADE NAO CANONICA"}, {1}, {2},
                 {false}, {false}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [InsumosEstoque]
                ([Id], [Nome], [NomeNormalizado], [Tipo], [UnidadeBase],
                 [EstoqueMinimo_ValorBase], [EstoqueMinimo_UnidadeBase], [EstoqueMinimo_Dimensao],
                 [ControlaLote], [ValidadeObrigatoria], [Ativo])
            VALUES
                ({Guid.NewGuid()}, {"Mínimo negativo"}, {"MINIMO NEGATIVO"}, {1}, {1},
                 {-1m}, {1}, {1}, {false}, {false}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [InsumosEstoque]
                ([Id], [Nome], [NomeNormalizado], [Tipo], [UnidadeBase],
                 [EstoqueMinimo_ValorBase], [EstoqueMinimo_UnidadeBase], [EstoqueMinimo_Dimensao],
                 [ControlaLote], [ValidadeObrigatoria], [Ativo])
            VALUES
                ({Guid.NewGuid()}, {"Contagem fracionária"}, {"CONTAGEM FRACIONARIA"}, {2}, {5},
                 {1.5m}, {5}, {3}, {false}, {false}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [InsumosEstoque]
                ([Id], [Nome], [NomeNormalizado], [Tipo], [UnidadeBase],
                 [EstoqueMinimo_ValorBase], [EstoqueMinimo_UnidadeBase], [EstoqueMinimo_Dimensao],
                 [ControlaLote], [ValidadeObrigatoria], [Ativo])
            VALUES
                ({Guid.NewGuid()}, {"Mínimo incompatível"}, {"MINIMO INCOMPATIVEL"}, {1}, {1},
                 {1m}, {3}, {2}, {false}, {false}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [FormasCompraInsumo]
                ([Id], [InsumoId], [Nome], [NomeNormalizado], [Fator_ValorBase],
                 [Fator_UnidadeBase], [Fator_Dimensao], [Ativa])
            VALUES
                ({Guid.NewGuid()}, {first.Id}, {"Fator fracionário"}, {"FATOR FRACIONARIO"},
                 {1.5m}, {5}, {3}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [FormasCompraInsumo]
                ([Id], [InsumoId], [Nome], [NomeNormalizado], [Fator_ValorBase],
                 [Fator_UnidadeBase], [Fator_Dimensao], [Ativa])
            VALUES
                ({Guid.NewGuid()}, {first.Id}, {"Fator zero"}, {"FATOR ZERO"},
                 {0m}, {1}, {1}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [FormasCompraInsumo]
                ([Id], [InsumoId], [Nome], [NomeNormalizado], [Fator_ValorBase],
                 [Fator_UnidadeBase], [Fator_Dimensao], [Ativa])
            VALUES
                ({Guid.NewGuid()}, {first.Id}, {"Dimensão incompatível"}, {"DIMENSAO INCOMPATIVEL"},
                 {1m}, {3}, {1}, {true});
            """));

        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [FormasCompraInsumo]
                ([Id], [InsumoId], [Nome], [NomeNormalizado], [Fator_ValorBase],
                 [Fator_UnidadeBase], [Fator_Dimensao], [Ativa])
            VALUES
                ({Guid.NewGuid()}, {first.Id}, {" "}, {" "},
                 {1m}, {1}, {1}, {true});
            """));
    }

    [SqlServerIntegrationFact]
    public async Task RowVersion_RejectsStaleConcurrentSupplyAndPurchaseFormUpdates()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid supplyId;
        Guid formId;
        await using (var setupContext = database.CreateVerificationContext())
        {
            var supply = InsumoEstoque.Criar("Óleo", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro);
            var form = supply.AdicionarFormaCompra("Frasco", 100m, UnidadeFisicaEstoque.Mililitro);
            supplyId = supply.Id;
            formId = form.Id;
            setupContext.InsumosEstoque.Add(supply);
            await setupContext.SaveChangesAsync();
        }

        await using (var firstContext = database.CreateVerificationContext())
        await using (var staleContext = database.CreateVerificationContext())
        {
            var first = await firstContext.InsumosEstoque.SingleAsync(item => item.Id == supplyId);
            var stale = await staleContext.InsumosEstoque.SingleAsync(item => item.Id == supplyId);
            first.AtualizarDados("Óleo vegetal", TipoInsumoEstoque.MateriaPrima, null, false, false);
            await firstContext.SaveChangesAsync();
            stale.DefinirAtivo(false);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => staleContext.SaveChangesAsync());
        }

        await using (var firstContext = database.CreateVerificationContext())
        await using (var staleContext = database.CreateVerificationContext())
        {
            var first = await firstContext.InsumosEstoque
                .Include(item => item.FormasCompra)
                .SingleAsync(item => item.Id == supplyId);
            var stale = await staleContext.InsumosEstoque
                .Include(item => item.FormasCompra)
                .SingleAsync(item => item.Id == supplyId);
            first.AtualizarFormaCompra(formId, "Frasco 150 ml", 150m, UnidadeFisicaEstoque.Mililitro);
            await firstContext.SaveChangesAsync();
            stale.AtualizarFormaCompra(formId, "Frasco 125 ml", 125m, UnidadeFisicaEstoque.Mililitro);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => staleContext.SaveChangesAsync());
        }
    }
}
