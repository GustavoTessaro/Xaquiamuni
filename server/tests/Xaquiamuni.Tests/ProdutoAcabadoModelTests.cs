using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public sealed class ProdutoAcabadoModelTests
{
    [Fact]
    public void Model_MapsVariantScopedLotsBalancesPoliciesAndConcurrency()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var variante = model.FindEntityType(typeof(PoliticaEstoqueProdutoVariante))!;
        var lote = model.FindEntityType(typeof(LoteProdutoAcabado))!;
        var saldo = model.FindEntityType(typeof(SaldoLoteProdutoAcabado))!;
        var varianteEntity = model.FindEntityType(typeof(Xaquiamuni.Dominio.Entidades.ProdutoVariante))!;
        var policyForeignKey = Assert.Single(variante.GetForeignKeys());
        var lotForeignKey = Assert.Single(lote.GetForeignKeys());
        var saldoForeignKey = Assert.Single(saldo.GetForeignKeys());

        Assert.Equal("PoliticasEstoqueProdutoVariante", variante.GetTableName());
        Assert.Equal("LotesProdutoAcabado", lote.GetTableName());
        Assert.Equal("SaldosProdutoAcabado", saldo.GetTableName());
        Assert.Equal([nameof(PoliticaEstoqueProdutoVariante.ProdutoVarianteId)],
            variante.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Same(varianteEntity, policyForeignKey.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Restrict, policyForeignKey.DeleteBehavior);
        Assert.Same(varianteEntity, lotForeignKey.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Restrict, lotForeignKey.DeleteBehavior);
        Assert.Equal("date", lote.FindProperty(nameof(LoteProdutoAcabado.DataFabricacao))!.GetColumnType());
        Assert.Equal("date", lote.FindProperty(nameof(LoteProdutoAcabado.DataValidade))!.GetColumnType());
        Assert.Contains(lote.GetKeys(), key =>
            key.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(LoteProdutoAcabado.ProdutoVarianteId),
                    nameof(LoteProdutoAcabado.Id)
                ]));
        Assert.True(lote.GetIndexes().Single(index =>
            index.GetDatabaseName() == "UX_LotesProdutoAcabado_Codigo").IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, saldoForeignKey.DeleteBehavior);
        Assert.Equal(
            [nameof(SaldoLoteProdutoAcabado.ProdutoVarianteId), nameof(SaldoLoteProdutoAcabado.LoteId)],
            saldoForeignKey.Properties.Select(property => property.Name));
        Assert.Equal(
            [nameof(LoteProdutoAcabado.ProdutoVarianteId), nameof(LoteProdutoAcabado.Id)],
            saldoForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.True(saldo.GetIndexes().Single(index =>
            index.GetDatabaseName() == "UX_SaldosProdutoAcabado_ProdutoVarianteId_LoteId").IsUnique);
        AssertConcurrencyToken(saldo);
        Assert.Contains(
            "CK_SaldosProdutoAcabado_Quantidades_NonNegative",
            saldo.GetCheckConstraints().Select(constraint => constraint.Name));
        Assert.Contains(
            "CK_SaldosProdutoAcabado_QuantidadeFisica_IntRange",
            saldo.GetCheckConstraints().Select(constraint => constraint.Name));
    }

    [Fact]
    public void Model_MapsAuditableMovementsIdempotencyAndRestrictiveReferences()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var movimento = model.FindEntityType(typeof(MovimentoProdutoAcabado))!;
        var noDifference = model.FindEntityType(typeof(ContagemSemDiferencaProdutoAcabado))!;
        var lotForeignKey = Assert.Single(movimento.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(LoteProdutoAcabado));
        var originalForeignKey = Assert.Single(movimento.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(MovimentoProdutoAcabado.MovimentoOriginalId)]));
        var idempotencyIndex = Assert.Single(movimento.GetIndexes(), index =>
            index.GetDatabaseName()
                == "UX_MovimentosProdutoAcabado_ResponsavelId_ChaveIdempotencia");

        Assert.Equal("MovimentosProdutoAcabado", movimento.GetTableName());
        Assert.Equal(DeleteBehavior.Restrict, lotForeignKey.DeleteBehavior);
        Assert.Equal(
            [nameof(MovimentoProdutoAcabado.ProdutoVarianteId), nameof(MovimentoProdutoAcabado.LoteId)],
            lotForeignKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, originalForeignKey.DeleteBehavior);
        Assert.True(idempotencyIndex.IsUnique);
        Assert.Equal(
            "Latin1_General_100_BIN2",
            movimento.FindProperty(nameof(MovimentoProdutoAcabado.ChaveIdempotencia))!.GetCollation());
        Assert.Equal(
            "datetimeoffset(7)",
            movimento.FindProperty(nameof(MovimentoProdutoAcabado.RegistradoEmUtc))!.GetColumnType());
        Assert.Equal(
            "binary(32)",
            movimento.FindProperty(nameof(MovimentoProdutoAcabado.FingerprintRequisicao))!.GetColumnType());
        Assert.Equal(
            "UX_MovimentosProdutoAcabado_MovimentoOriginalId",
            movimento.GetIndexes().Single(index =>
                index.GetDatabaseName() == "UX_MovimentosProdutoAcabado_MovimentoOriginalId")
                .GetDatabaseName());
        Assert.Equal(
            "ContagensSemDiferencaProdutoAcabado",
            noDifference.GetTableName());
        Assert.Equal(2, noDifference.FindPrimaryKey()!.Properties.Count);
        Assert.Equal(
            "binary(32)",
            noDifference.FindProperty(nameof(ContagemSemDiferencaProdutoAcabado.FingerprintRequisicao))!
                .GetColumnType());
    }

    private static void AssertConcurrencyToken(IEntityType entity)
    {
        var version = entity.FindProperty("Versao")!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("rowversion", version.GetColumnType());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
    }

    private static XaquiamuniDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniFinishedGoodsModelTest")
            .Options;
        return new XaquiamuniDbContext(options);
    }
}
