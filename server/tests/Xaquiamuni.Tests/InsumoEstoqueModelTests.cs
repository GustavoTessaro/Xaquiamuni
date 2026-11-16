using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public sealed class InsumoEstoqueModelTests
{
    [Fact]
    public void Model_MapsQuantitiesRelationshipIndexesAndConcurrencyTokens()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var insumo = model.FindEntityType(typeof(InsumoEstoque))!;
        var forma = model.FindEntityType(typeof(FormaCompraInsumo))!;
        var minimum = insumo.FindNavigation(nameof(InsumoEstoque.EstoqueMinimo))!.TargetEntityType;
        var factor = forma.FindNavigation(nameof(FormaCompraInsumo.QuantidadeEquivalentePorEmbalagem))!
            .TargetEntityType;
        var uniqueIndex = Assert.Single(forma.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(FormaCompraInsumo.InsumoId), nameof(FormaCompraInsumo.NomeNormalizado)]));

        Assert.Equal("InsumosEstoque", insumo.GetTableName());
        Assert.Equal("FormasCompraInsumo", forma.GetTableName());
        Assert.Null(insumo.FindIndex(nameof(InsumoEstoque.NomeNormalizado)));
        Assert.True(uniqueIndex.IsUnique);
        Assert.Equal("UX_FormasCompraInsumo_InsumoId_NomeNormalizado", uniqueIndex.GetDatabaseName());
        Assert.False(insumo.FindNavigation(nameof(InsumoEstoque.EstoqueMinimo))!.IsCollection);
        Assert.Equal(18, minimum.FindProperty(nameof(QuantidadeEstoque.ValorBase))!.GetPrecision());
        Assert.Equal(6, minimum.FindProperty(nameof(QuantidadeEstoque.ValorBase))!.GetScale());
        Assert.Equal(18, factor.FindProperty(nameof(QuantidadeEstoque.ValorBase))!.GetPrecision());
        Assert.Equal(6, factor.FindProperty(nameof(QuantidadeEstoque.ValorBase))!.GetScale());
        AssertConcurrencyToken(insumo);
        AssertConcurrencyToken(forma);
    }

    [Fact]
    public void Model_UsesRestrictiveForeignKeyAndRequiredPurchaseFactor()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var insumo = model.FindEntityType(typeof(InsumoEstoque))!;
        var forma = model.FindEntityType(typeof(FormaCompraInsumo))!;
        var foreignKey = Assert.Single(forma.GetForeignKeys());
        var minimum = insumo.FindNavigation(nameof(InsumoEstoque.EstoqueMinimo))!.TargetEntityType;
        var factor = forma.FindNavigation(nameof(FormaCompraInsumo.QuantidadeEquivalentePorEmbalagem))!
            .TargetEntityType;

        Assert.Same(insumo, foreignKey.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsRequired);
        Assert.True(factor.FindOwnership()!.IsRequired);
        Assert.False(factor.FindProperty(nameof(QuantidadeEstoque.ValorBase))!.IsNullable);
    }

    [Fact]
    public void Model_MapsLotDatesIdentityAndConcurrency()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var lot = model.FindEntityType(typeof(LoteInsumo))!;
        var foreignKey = Assert.Single(lot.GetForeignKeys());

        Assert.Equal("LotesInsumo", lot.GetTableName());
        Assert.Equal("date", lot.FindProperty(nameof(LoteInsumo.DataEntrada))!.GetColumnType());
        Assert.Equal("date", lot.FindProperty(nameof(LoteInsumo.DataValidade))!.GetColumnType());
        Assert.True(lot.FindProperty(nameof(LoteInsumo.DataValidade))!.IsNullable);
        Assert.Contains(lot.GetKeys(), key =>
            key.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(LoteInsumo.InsumoId), nameof(LoteInsumo.Id)]));
        Assert.DoesNotContain(lot.GetIndexes(), index => index.IsUnique);
        Assert.Same(model.FindEntityType(typeof(InsumoEstoque)), foreignKey.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        AssertConcurrencyToken(lot);
    }

    [Fact]
    public void Model_MapsSingleBalanceSourceWithFilteredUniquenessAndValidations()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var balance = model.FindEntityType(typeof(SaldoInsumo))!;
        var quantity = balance.FindNavigation(nameof(SaldoInsumo.QuantidadeFisica))!.TargetEntityType;
        var indexes = balance.GetIndexes().ToArray();
        var aggregateIndex = Assert.Single(indexes, index =>
            index.GetDatabaseName() == "UX_SaldosInsumo_InsumoId_Agregado");
        var lotIndex = Assert.Single(indexes, index =>
            index.GetDatabaseName() == "UX_SaldosInsumo_InsumoId_LoteId");
        var foreignKeys = balance.GetForeignKeys().ToArray();

        Assert.Equal("SaldosInsumo", balance.GetTableName());
        Assert.Equal(18, quantity.FindProperty(nameof(QuantidadeEstoque.ValorBase))!.GetPrecision());
        Assert.Equal(6, quantity.FindProperty(nameof(QuantidadeEstoque.ValorBase))!.GetScale());
        Assert.True(aggregateIndex.IsUnique);
        Assert.Equal("[LoteId] IS NULL", aggregateIndex.GetFilter());
        Assert.True(lotIndex.IsUnique);
        Assert.Equal("[LoteId] IS NOT NULL", lotIndex.GetFilter());
        Assert.Contains(foreignKeys, foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(InsumoEstoque)
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(foreignKeys, foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(LoteInsumo)
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict
            && foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(SaldoInsumo.InsumoId), nameof(SaldoInsumo.LoteId)]));
        AssertConcurrencyToken(balance);
    }

    [Fact]
    public void ModelSnapshot_HasNoPendingChanges()
    {
        using var context = CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static void AssertConcurrencyToken(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity)
    {
        var version = entity.FindProperty("Versao")!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("rowversion", version.GetColumnType());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
    }

    private static XaquiamuniDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniInsumoModelTest")
            .Options;
        return new XaquiamuniDbContext(options);
    }
}
