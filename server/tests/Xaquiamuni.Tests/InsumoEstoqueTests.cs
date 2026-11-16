using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class InsumoEstoqueTests
{
    [Theory]
    [InlineData(TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama, DimensaoFisicaEstoque.Massa)]
    [InlineData(TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro, DimensaoFisicaEstoque.Volume)]
    [InlineData(TipoInsumoEstoque.Embalagem, UnidadeFisicaEstoque.Unidade, DimensaoFisicaEstoque.Contagem)]
    public void Criar_AcceptsCanonicalBaseUnitsWithoutCreatingStock(
        TipoInsumoEstoque tipo,
        UnidadeFisicaEstoque unidadeBase,
        DimensaoFisicaEstoque dimensao)
    {
        var insumo = InsumoEstoque.Criar("  Base   glicerinada ", tipo, unidadeBase);

        Assert.NotEqual(Guid.Empty, insumo.Id);
        Assert.Equal("Base glicerinada", insumo.Nome);
        Assert.Equal("BASE GLICERINADA", insumo.NomeNormalizado);
        Assert.Equal(tipo, insumo.Tipo);
        Assert.Equal(unidadeBase, insumo.UnidadeBase);
        Assert.Equal(dimensao, QuantidadeEstoque.Criar(0m, unidadeBase).Dimensao);
        Assert.Null(insumo.EstoqueMinimo);
        Assert.Empty(insumo.FormasCompra);
        Assert.True(insumo.Ativo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Criar_RejectsMissingName(string? nome)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => InsumoEstoque.Criar(nome, TipoInsumoEstoque.Outro, UnidadeFisicaEstoque.Grama));

        Assert.Equal("nome", exception.ParamName);
    }

    [Theory]
    [InlineData(UnidadeFisicaEstoque.Quilograma)]
    [InlineData(UnidadeFisicaEstoque.Litro)]
    [InlineData((UnidadeFisicaEstoque)99)]
    public void Criar_RejectsNonCanonicalOrUndefinedBaseUnit(UnidadeFisicaEstoque unidadeBase)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InsumoEstoque.Criar("Essência", TipoInsumoEstoque.MateriaPrima, unidadeBase));
    }

    [Fact]
    public void Criar_RejectsUndefinedSupplyType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InsumoEstoque.Criar("Essência", (TipoInsumoEstoque)99, UnidadeFisicaEstoque.Mililitro));
    }

    [Fact]
    public void Criar_AcceptsMissingZeroAndPositiveMinimumInBaseUnit()
    {
        var zero = InsumoEstoque.Criar(
            "Potes",
            TipoInsumoEstoque.Embalagem,
            UnidadeFisicaEstoque.Unidade,
            QuantidadeEstoque.Criar(0m, UnidadeFisicaEstoque.Unidade));
        var positive = InsumoEstoque.Criar(
            "Essência",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            QuantidadeEstoque.Criar(250m, UnidadeFisicaEstoque.Mililitro));
        var normalizedToBase = InsumoEstoque.Criar(
            "Cera",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            QuantidadeEstoque.Criar(1m, UnidadeFisicaEstoque.Quilograma));

        Assert.Null(InsumoEstoque.Criar(
            "Corante",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama).EstoqueMinimo);
        Assert.Equal(0m, zero.EstoqueMinimo?.ValorBase);
        Assert.Equal(250m, positive.EstoqueMinimo?.ValorBase);
        Assert.Equal(UnidadeFisicaEstoque.Grama, normalizedToBase.EstoqueMinimo?.UnidadeBase);
        Assert.Equal(1000m, normalizedToBase.EstoqueMinimo?.ValorBase);
    }

    [Fact]
    public void Criar_RejectsMinimumFromAnotherDimension()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            InsumoEstoque.Criar(
                "Essência",
                TipoInsumoEstoque.MateriaPrima,
                UnidadeFisicaEstoque.Mililitro,
                QuantidadeEstoque.Criar(10m, UnidadeFisicaEstoque.Grama)));

        Assert.Equal("estoqueMinimo", exception.ParamName);
    }

    [Fact]
    public void MinimumQuantity_RejectsNegativeAndFractionalCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(-1m, UnidadeFisicaEstoque.Grama));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuantidadeEstoque.Criar(1.5m, UnidadeFisicaEstoque.Unidade));
    }

    [Fact]
    public void Criar_ValidatesLotAndExpiryConfiguration()
    {
        var trackedWithoutRequiredExpiry = InsumoEstoque.Criar(
            "Óleo vegetal",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true);
        var trackedWithRequiredExpiry = InsumoEstoque.Criar(
            "Essência",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true,
            validadeObrigatoria: true);

        Assert.True(trackedWithoutRequiredExpiry.ControlaLote);
        Assert.False(trackedWithoutRequiredExpiry.ValidadeObrigatoria);
        Assert.True(trackedWithRequiredExpiry.ControlaLote);
        Assert.True(trackedWithRequiredExpiry.ValidadeObrigatoria);
    }

    [Fact]
    public void Criar_RejectsRequiredExpiryWithoutLotControl()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            InsumoEstoque.Criar(
                "Cera",
                TipoInsumoEstoque.MateriaPrima,
                UnidadeFisicaEstoque.Grama,
                validadeObrigatoria: true));

        Assert.Equal("validadeObrigatoria", exception.ParamName);
    }

    [Fact]
    public void AtualizarDados_CannotChangeLotPolicyOutsideProtectedOperation()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);

        Assert.Throws<InvalidOperationException>(() => insumo.AtualizarDados(
            "Cera de abelha",
            TipoInsumoEstoque.MateriaPrima,
            QuantidadeEstoque.Criar(100m, UnidadeFisicaEstoque.Grama),
            controlaLote: true,
            validadeObrigatoria: true));

        Assert.Equal("Cera", insumo.Nome);
        Assert.Equal(UnidadeFisicaEstoque.Grama, insumo.UnidadeBase);
        Assert.Null(insumo.EstoqueMinimo);
        Assert.False(insumo.ControlaLote);
        Assert.False(insumo.ValidadeObrigatoria);
    }

    [Fact]
    public void AtualizarDados_RejectsInvalidConfigurationWithoutPartialChanges()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);

        Assert.Throws<ArgumentException>(() =>
            insumo.AtualizarDados(
                "Outro nome",
                TipoInsumoEstoque.Embalagem,
                null,
                controlaLote: false,
                validadeObrigatoria: true));

        Assert.Equal("Cera", insumo.Nome);
        Assert.Equal(TipoInsumoEstoque.MateriaPrima, insumo.Tipo);
        Assert.False(insumo.ControlaLote);
        Assert.False(insumo.ValidadeObrigatoria);
    }

    [Fact]
    public void AdicionarFormaCompra_AllowsMultipleFormsAndNormalizesFactors()
    {
        var massa = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var volume = CriarInsumo(UnidadeFisicaEstoque.Mililitro);
        var contagem = CriarInsumo(UnidadeFisicaEstoque.Unidade);

        var pacotePequeno = massa.AdicionarFormaCompra("Pacote de 500 g", 500m, UnidadeFisicaEstoque.Grama);
        var pacoteGrande = massa.AdicionarFormaCompra("Pacote de 1 kg", 1m, UnidadeFisicaEstoque.Quilograma);
        var frasco = volume.AdicionarFormaCompra("Frasco de 1 L", 1m, UnidadeFisicaEstoque.Litro);
        var caixa = contagem.AdicionarFormaCompra("Caixa com 12 potes", 12m, UnidadeFisicaEstoque.Unidade);

        Assert.Equal(2, massa.FormasCompra.Count);
        Assert.Equal(500m, pacotePequeno.QuantidadeEquivalentePorEmbalagem.ValorBase);
        Assert.Equal(1000m, pacoteGrande.QuantidadeEquivalentePorEmbalagem.ValorBase);
        Assert.Equal(UnidadeFisicaEstoque.Grama, pacoteGrande.QuantidadeEquivalentePorEmbalagem.UnidadeBase);
        Assert.Equal(1000m, frasco.QuantidadeEquivalentePorEmbalagem.ValorBase);
        Assert.Equal(12m, caixa.QuantidadeEquivalentePorEmbalagem.ValorBase);
        Assert.All(massa.FormasCompra, forma => Assert.True(forma.Ativa));
    }

    [Theory]
    [InlineData(0, UnidadeFisicaEstoque.Grama)]
    [InlineData(-1, UnidadeFisicaEstoque.Grama)]
    [InlineData(1, UnidadeFisicaEstoque.Mililitro)]
    [InlineData(0.0000001, UnidadeFisicaEstoque.Grama)]
    [InlineData(0.5, UnidadeFisicaEstoque.Unidade)]
    public void AdicionarFormaCompra_RejectsInvalidFactor(decimal fator, UnidadeFisicaEstoque unidade)
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);

        Assert.ThrowsAny<ArgumentException>(
            () => insumo.AdicionarFormaCompra("Pacote", fator, unidade));
    }

    [Fact]
    public void AdicionarFormaCompra_RejectsDuplicateNormalizedNameWithinSameInsumo()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);
        insumo.AdicionarFormaCompra("Pacote de 500 g", 500m, UnidadeFisicaEstoque.Grama);

        Assert.Throws<InvalidOperationException>(() =>
            insumo.AdicionarFormaCompra("  PACOTE   DE 500 G ", 1m, UnidadeFisicaEstoque.Quilograma));
    }

    [Fact]
    public void AdicionarFormaCompra_AllowsSameNameForDifferentSupplies()
    {
        var baseGlicerinada = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var cera = InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);

        var primeira = baseGlicerinada.AdicionarFormaCompra("Pacote", 500m, UnidadeFisicaEstoque.Grama);
        var segunda = cera.AdicionarFormaCompra("Pacote", 1m, UnidadeFisicaEstoque.Quilograma);

        Assert.NotEqual(primeira.Id, segunda.Id);
        Assert.NotEqual(primeira.InsumoId, segunda.InsumoId);
    }

    [Fact]
    public void AtualizarFormaCompra_ValidatesNameAndFactorAndKeepsExistingSnapshot()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var forma = insumo.AdicionarFormaCompra("Pacote de 500 g", 500m, UnidadeFisicaEstoque.Grama);
        var snapshot = insumo.ConverterCompra(forma.Id, 3m);

        insumo.AtualizarFormaCompra(
            forma.Id,
            "Pacote de 1 kg",
            1m,
            UnidadeFisicaEstoque.Quilograma);

        Assert.Equal("Pacote de 1 kg", forma.Nome);
        Assert.Equal(1000m, forma.QuantidadeEquivalentePorEmbalagem.ValorBase);
        Assert.Equal("Pacote de 500 g", snapshot.NomeFormaCompra);
        Assert.Equal(500m, snapshot.FatorAplicado.ValorBase);
        Assert.Equal(1500m, snapshot.QuantidadeBase.ValorBase);
    }

    [Fact]
    public void AtualizarFormaCompra_RejectsDuplicateNameIncludingInactiveForms()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var primeira = insumo.AdicionarFormaCompra("Pacote", 500m, UnidadeFisicaEstoque.Grama);
        var segunda = insumo.AdicionarFormaCompra("Caixa", 1000m, UnidadeFisicaEstoque.Grama);
        insumo.DefinirFormaCompraAtiva(primeira.Id, false);

        Assert.Throws<InvalidOperationException>(() =>
            insumo.AtualizarFormaCompra(
                segunda.Id,
                " pacote ",
                750m,
                UnidadeFisicaEstoque.Grama));
    }

    [Theory]
    [InlineData(3, 5000, 15000)]
    [InlineData(2, 100, 200)]
    [InlineData(4, 12, 48)]
    public void ConverterCompra_ConvertsConfiguredMassVolumeAndCountForms(
        decimal quantidadeEmbalagens,
        decimal fator,
        decimal esperado)
    {
        var unidade = fator == 12m
            ? UnidadeFisicaEstoque.Unidade
            : fator == 100m
                ? UnidadeFisicaEstoque.Mililitro
                : UnidadeFisicaEstoque.Grama;
        var insumo = CriarInsumo(unidade);
        var forma = insumo.AdicionarFormaCompra("Embalagem", fator, unidade);

        var resultado = insumo.ConverterCompra(forma.Id, quantidadeEmbalagens);

        Assert.Equal(quantidadeEmbalagens, resultado.QuantidadeEmbalagens);
        Assert.Equal(insumo.Id, resultado.InsumoId);
        Assert.Equal(forma.Id, resultado.FormaCompraId);
        Assert.Equal(forma.Nome, resultado.NomeFormaCompra);
        Assert.Equal(fator, resultado.FatorAplicado.ValorBase);
        Assert.Equal(esperado, resultado.QuantidadeBase.ValorBase);
        Assert.Equal(unidade, resultado.QuantidadeBase.UnidadeBase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    public void ConverterCompra_RejectsNonPositiveOrFractionalPackageCount(decimal quantidade)
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var forma = insumo.AdicionarFormaCompra("Pacote", 500m, UnidadeFisicaEstoque.Grama);

        Assert.Throws<ArgumentOutOfRangeException>(() => insumo.ConverterCompra(forma.Id, quantidade));
    }

    [Fact]
    public void ConverterCompra_RejectsForeignInactiveOrInactiveSupply()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var outroInsumo = InsumoEstoque.Criar("Outro", TipoInsumoEstoque.Outro, UnidadeFisicaEstoque.Grama);
        var forma = insumo.AdicionarFormaCompra("Pacote", 500m, UnidadeFisicaEstoque.Grama);
        var formaAlheia = outroInsumo.AdicionarFormaCompra("Pacote", 500m, UnidadeFisicaEstoque.Grama);

        Assert.Throws<ArgumentException>(() => insumo.ConverterCompra(formaAlheia.Id, 1m));
        insumo.DefinirFormaCompraAtiva(forma.Id, false);
        Assert.Throws<InvalidOperationException>(() => insumo.ConverterCompra(forma.Id, 1m));
        insumo.DefinirFormaCompraAtiva(forma.Id, true);
        insumo.DefinirAtivo(false);
        Assert.Throws<InvalidOperationException>(() => insumo.ConverterCompra(forma.Id, 1m));
    }

    [Fact]
    public void ConverterCompra_RejectsResultAboveQuantityLimit()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var forma = insumo.AdicionarFormaCompra(
            "Embalagem grande",
            QuantidadeEstoque.MaximoNaUnidadeBase,
            UnidadeFisicaEstoque.Grama);

        Assert.Throws<ArgumentOutOfRangeException>(() => insumo.ConverterCompra(forma.Id, 2m));
    }

    [Fact]
    public void ConverterCompra_RejectsDecimalOverflow()
    {
        var insumo = CriarInsumo(UnidadeFisicaEstoque.Grama);
        var forma = insumo.AdicionarFormaCompra("Pacote", 500m, UnidadeFisicaEstoque.Grama);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => insumo.ConverterCompra(forma.Id, decimal.MaxValue));
    }

    private static InsumoEstoque CriarInsumo(UnidadeFisicaEstoque unidadeBase) =>
        InsumoEstoque.Criar("Cera", TipoInsumoEstoque.MateriaPrima, unidadeBase);
}
