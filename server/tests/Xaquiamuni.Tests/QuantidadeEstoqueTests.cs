using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class QuantidadeEstoqueTests
{
    [Fact]
    public void Criar_ConvertsMassToAndFromBaseUnit()
    {
        var fromKilograms = QuantidadeEstoque.Criar(1.25m, UnidadeFisicaEstoque.Quilograma);
        var fromGrams = QuantidadeEstoque.Criar(1250m, UnidadeFisicaEstoque.Grama);

        Assert.Equal(DimensaoFisicaEstoque.Massa, fromKilograms.Dimensao);
        Assert.Equal(UnidadeFisicaEstoque.Grama, fromKilograms.UnidadeBase);
        Assert.Equal(1250m, fromKilograms.ValorBase);
        Assert.Equal(1.25m, fromKilograms.ConverterPara(UnidadeFisicaEstoque.Quilograma));
        Assert.Equal(fromKilograms, fromGrams);
    }

    [Fact]
    public void Criar_ConvertsVolumeToAndFromBaseUnit()
    {
        var fromLiters = QuantidadeEstoque.Criar(0.75m, UnidadeFisicaEstoque.Litro);
        var fromMilliliters = QuantidadeEstoque.Criar(750m, UnidadeFisicaEstoque.Mililitro);

        Assert.Equal(DimensaoFisicaEstoque.Volume, fromLiters.Dimensao);
        Assert.Equal(UnidadeFisicaEstoque.Mililitro, fromLiters.UnidadeBase);
        Assert.Equal(750m, fromLiters.ValorBase);
        Assert.Equal(0.75m, fromLiters.ConverterPara(UnidadeFisicaEstoque.Litro));
        Assert.Equal(fromLiters, fromMilliliters);
    }

    [Fact]
    public void Criar_AllowsFractionalValuesWhenBaseQuantityIsExactlyRepresentable()
    {
        var quantity = QuantidadeEstoque.Criar(0.0000001m, UnidadeFisicaEstoque.Quilograma);

        Assert.Equal(0.0001m, quantity.ValorBase);
        Assert.Equal(0.0000001m, quantity.ConverterPara(UnidadeFisicaEstoque.Quilograma));
    }

    [Fact]
    public void Criar_KeepsCountAsAnIntegerBaseQuantity()
    {
        var quantity = QuantidadeEstoque.Criar(12m, UnidadeFisicaEstoque.Unidade);

        Assert.Equal(DimensaoFisicaEstoque.Contagem, quantity.Dimensao);
        Assert.Equal(UnidadeFisicaEstoque.Unidade, quantity.UnidadeBase);
        Assert.Equal(12m, quantity.ValorBase);
        Assert.Equal(12m, quantity.ConverterPara(UnidadeFisicaEstoque.Unidade));
    }

    [Theory]
    [InlineData(UnidadeFisicaEstoque.Quilograma, UnidadeFisicaEstoque.Litro)]
    [InlineData(UnidadeFisicaEstoque.Litro, UnidadeFisicaEstoque.Unidade)]
    [InlineData(UnidadeFisicaEstoque.Grama, UnidadeFisicaEstoque.Unidade)]
    public void ConverterPara_RejectsIncompatibleDimensions(
        UnidadeFisicaEstoque unidadeOrigem,
        UnidadeFisicaEstoque unidadeDestino)
    {
        var quantity = QuantidadeEstoque.Criar(1m, unidadeOrigem);

        var exception = Assert.Throws<ArgumentException>(
            () => quantity.ConverterPara(unidadeDestino));

        Assert.Equal("unidadeDestino", exception.ParamName);
    }

    [Fact]
    public void Criar_RejectsUndefinedUnit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(1m, (UnidadeFisicaEstoque)99));
    }

    [Fact]
    public void Criar_RejectsNegativeQuantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(-0.1m, UnidadeFisicaEstoque.Grama));
    }

    [Fact]
    public void Criar_RejectsFractionalCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(1.5m, UnidadeFisicaEstoque.Unidade));
    }

    [Fact]
    public void Criar_AllowsZeroBalanceButPositiveValidationRejectsIt()
    {
        var zero = QuantidadeEstoque.Criar(0m, UnidadeFisicaEstoque.Mililitro);

        Assert.Equal(0m, zero.ValorBase);
        Assert.Throws<ArgumentOutOfRangeException>(() => zero.ExigirPositiva());
    }

    [Fact]
    public void Criar_EnforcesDecimal18Scale6MagnitudeAndExactBasePrecision()
    {
        var maximum = QuantidadeEstoque.Criar(
            QuantidadeEstoque.MaximoNaUnidadeBase,
            UnidadeFisicaEstoque.Grama);
        var maximumFromKilograms = QuantidadeEstoque.Criar(
            QuantidadeEstoque.MaximoNaUnidadeBase / 1000m,
            UnidadeFisicaEstoque.Quilograma);
        var sixDecimalPlaces = QuantidadeEstoque.Criar(0.000001m, UnidadeFisicaEstoque.Grama);

        Assert.Equal(QuantidadeEstoque.MaximoNaUnidadeBase, maximum.ValorBase);
        Assert.Equal(QuantidadeEstoque.MaximoNaUnidadeBase, maximumFromKilograms.ValorBase);
        Assert.Equal(0.000001m, sixDecimalPlaces.ValorBase);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(
                QuantidadeEstoque.MaximoNaUnidadeBase + 0.000001m,
                UnidadeFisicaEstoque.Grama));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(0.0000001m, UnidadeFisicaEstoque.Grama));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(0.0000000001m, UnidadeFisicaEstoque.Quilograma));
    }

    [Fact]
    public void Criar_RejectsDecimalOverflowDuringConversion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(decimal.MaxValue, UnidadeFisicaEstoque.Quilograma));
    }

    [Fact]
    public void StockUnitsRemainSeparateFromCatalogMeasurementsAndSalesUnits()
    {
        Assert.Equal("UN", UnidadeVendaCatalogo.Normalizar(" un "));
        Assert.Equal(1000m, MedidaFisicaCatalogo.ConverterPesoParaGramas(1m, "kg"));
        Assert.Equal((short)1, (short)TipoMedidaFisica.PesoLiquido);
        Assert.Equal((short)2, (short)TipoMedidaFisica.VolumeLiquido);
        Assert.Equal(
            1.25m,
            QuantidadeEstoque.Criar(1.25m, UnidadeFisicaEstoque.Quilograma)
                .ConverterPara(UnidadeFisicaEstoque.Quilograma));
    }
}
