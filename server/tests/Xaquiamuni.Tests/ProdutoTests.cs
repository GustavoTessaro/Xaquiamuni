using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class ProdutoTests
{
    [Fact]
    public void CriarSimples_CreatesUnpublishedProductWithActiveDefaultVariant()
    {
        var categoria = Categoria.Criar("Sabonetes", null, 0);

        var produto = Produto.CriarSimples(
            "  Sabonete   de Lavanda ",
            "  Feito à mão ",
            categoria,
            " un ",
            19.90m);

        var variante = Assert.Single(produto.Variantes);
        Assert.Equal("Sabonete de Lavanda", produto.Nome);
        Assert.Equal("Feito à mão", produto.Descricao);
        Assert.Equal(categoria.Id, produto.CategoriaId);
        Assert.False(produto.Publicado);
        Assert.Equal(Produto.IdentificacaoVariantePadrao, variante.Identificacao);
        Assert.Equal("UN", variante.UnidadeVenda);
        Assert.Equal(19.90m, variante.PrecoVenda);
        Assert.True(variante.Ativa);
        Assert.True(variante.Padrao);
        Assert.Equal(produto.Id, variante.ProdutoId);
    }

    [Fact]
    public void CriarSimples_AllowsOptionalCanonicalPhysicalMeasurements()
    {
        var product = Produto.CriarSimples(
            "Sabonete para Descarrego",
            null,
            CategoriaAtiva(),
            "UN",
            15m,
            pesoLiquidoGramas: MedidaFisicaCatalogo.ConverterPesoParaGramas(0.1m, "kg"),
            volumeLiquidoMililitros: MedidaFisicaCatalogo.ConverterVolumeParaMililitros(0.05m, "L"));
        var variant = Assert.Single(product.Variantes);

        Assert.Equal(100m, variant.PesoLiquidoGramas);
        Assert.Equal(50m, variant.VolumeLiquidoMililitros);
    }

    [Fact]
    public void CriarSimples_AllowsMissingPhysicalMeasurements()
    {
        var product = Produto.CriarSimples("Sabonete", null, CategoriaAtiva(), "UN", 15m);
        var variant = Assert.Single(product.Variantes);

        Assert.Null(variant.PesoLiquidoGramas);
        Assert.Null(variant.VolumeLiquidoMililitros);
    }

    [Fact]
    public void CaracteristicasSupportCustomValuesAndOnlyCommercializedCombinations()
    {
        var product = CriarProdutoComDuasVariantes();
        var peso = product.AdicionarCaracteristica("Peso", TipoMedidaFisica.PesoLiquido);
        var lavanda = product.AdicionarCaracteristica("Fragrância");
        var weight100 = peso.AdicionarValorMedida(100m, "g");
        var weight200 = peso.AdicionarValorMedida(0.2m, "kg");
        var lavender = lavanda.AdicionarValor("Lavanda");
        var vanilla = lavanda.AdicionarValor("Baunilha");
        var variants = product.Variantes.ToArray();

        product.AssociarValorCaracteristica(variants[0].Id, peso.Id, weight100.Id);
        product.AssociarValorCaracteristica(variants[0].Id, lavanda.Id, lavender.Id);
        product.AssociarValorCaracteristica(variants[1].Id, peso.Id, weight200.Id);
        product.AssociarValorCaracteristica(variants[1].Id, lavanda.Id, vanilla.Id);

        Assert.Equal(2, product.Caracteristicas.Count);
        Assert.Equal(2, variants[0].CaracteristicaValores.Count);
        Assert.Equal(2, variants[1].CaracteristicaValores.Count);
        Assert.Equal("100 g", weight100.Nome);
        Assert.Equal("200 g", weight200.Nome);
        Assert.Equal(100m, variants[0].PesoLiquidoGramas);
        Assert.Equal(200m, variants[1].PesoLiquidoGramas);
    }

    [Fact]
    public void Caracteristicas_NormalizeNamesAndRejectEquivalentNamesAndValues()
    {
        var product = Produto.CriarSimples("Produto", null, CategoriaAtiva(), "UN", 10m);
        var characteristic = product.AdicionarCaracteristica("  Acabamento   externo ");

        Assert.Equal("Acabamento externo", characteristic.Nome);
        Assert.Throws<InvalidOperationException>(
            () => product.AdicionarCaracteristica("acabamento externo"));

        characteristic.AdicionarValor("  Brilho   fosco ");
        Assert.Throws<InvalidOperationException>(
            () => characteristic.AdicionarValor("BRILHO FOSCO"));
    }

    [Fact]
    public void AssociarValorCaracteristica_RejectsDuplicateValuesAndForeignProductValues()
    {
        var product = CriarProdutoComDuasVariantes();
        var variant = product.Variantes.First();
        var characteristic = product.AdicionarCaracteristica("Cor");
        var white = characteristic.AdicionarValor("Branca");
        var purple = characteristic.AdicionarValor("Roxa");
        product.AssociarValorCaracteristica(variant.Id, characteristic.Id, white.Id);

        var duplicateCharacteristic = Assert.Throws<InvalidOperationException>(
            () => product.AssociarValorCaracteristica(variant.Id, characteristic.Id, purple.Id));
        Assert.Contains("já possui", duplicateCharacteristic.Message);
        Assert.Single(variant.CaracteristicaValores);

        var otherProduct = Produto.CriarSimples("Outro produto", null, CategoriaAtiva(), "UN", 10m);
        var otherCharacteristic = otherProduct.AdicionarCaracteristica("Cor");
        var otherValue = otherCharacteristic.AdicionarValor("Branca");
        var foreignValue = Assert.Throws<ArgumentException>(
            () => product.AssociarValorCaracteristica(variant.Id, characteristic.Id, otherValue.Id));

        Assert.Equal("valorId", foreignValue.ParamName);
    }

    [Fact]
    public void AssociarValorCaracteristica_RejectsDuplicateCombinations()
    {
        var product = CriarProdutoComDuasVariantes();
        var material = product.AdicionarCaracteristica("Material");
        var fragrance = product.AdicionarCaracteristica("Fragrância");
        var glass = material.AdicionarValor("Vidro");
        var lavender = fragrance.AdicionarValor("Lavanda");
        var variants = product.Variantes.ToArray();
        product.AssociarValorCaracteristica(variants[0].Id, material.Id, glass.Id);
        product.AssociarValorCaracteristica(variants[0].Id, fragrance.Id, lavender.Id);
        product.AssociarValorCaracteristica(variants[1].Id, fragrance.Id, lavender.Id);

        var duplicate = Assert.Throws<InvalidOperationException>(
            () => product.AssociarValorCaracteristica(variants[1].Id, material.Id, glass.Id));

        Assert.Contains("combinação", duplicate.Message);
        Assert.Single(variants[1].CaracteristicaValores);
    }

    [Fact]
    public void MedidaFisica_RejectsNonPositiveOrUnsupportedPrecisionValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MedidaFisicaCatalogo.ValidarPesoCanonico(0m));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MedidaFisicaCatalogo.ValidarVolumeCanonico(10.0001m));
        Assert.Throws<ArgumentException>(
            () => MedidaFisicaCatalogo.ConverterPesoParaGramas(1m, "ML"));
        Assert.Throws<ArgumentException>(
            () => MedidaFisicaCatalogo.ConverterVolumeParaMililitros(1m, "KG"));
    }

    [Fact]
    public void MedidaFisicaCaracteristica_IsTheSingleSourceForVariantMeasure()
    {
        var product = CriarProdutoComDuasVariantes(pesoPadrao: 100m);
        var variant = product.Variantes.First();
        var characteristic = product.AdicionarCaracteristica("Conteúdo", TipoMedidaFisica.PesoLiquido);
        var weight = characteristic.AdicionarValorMedida(100m, "g");

        product.AssociarValorCaracteristica(variant.Id, characteristic.Id, weight.Id);
        variant.DefinirMedidasLiquidas(100m, null);

        Assert.Equal(100m, variant.PesoLiquidoGramas);
        Assert.Throws<InvalidOperationException>(() => variant.DefinirMedidasLiquidas(200m, null));
        Assert.Equal(100m, variant.PesoLiquidoGramas);
    }

    [Fact]
    public void Criar_AllowsMultipleVariantsWithExactlyOneActiveDefault()
    {
        var categoria = Categoria.Criar("Velas", null, 0);
        var padrao = ProdutoVariante.Criar("  200   g ", "UN", 25.50m, padrao: true);
        var outra = ProdutoVariante.Criar("400 g", "KIT", 42m, ativa: false);

        var produto = Produto.Criar("Vela", null, categoria, [padrao, outra]);

        Assert.Equal(2, produto.Variantes.Count);
        Assert.Single(produto.Variantes, variante => variante.Padrao && variante.Ativa);
        Assert.Equal(produto.Id, outra.ProdutoId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Criar_RejectsMissingProductName(string? nome)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Produto.CriarSimples(nome, null, CategoriaAtiva(), "UN", 1m));

        Assert.Equal("nome", exception.ParamName);
    }

    [Fact]
    public void Criar_RejectsNamesAndDescriptionsBeyondLimits()
    {
        var nameException = Assert.Throws<ArgumentException>(
            () => Produto.CriarSimples(
                new string('a', Produto.NomeMaxLength + 1),
                null,
                CategoriaAtiva(),
                "UN",
                1m));
        var descriptionException = Assert.Throws<ArgumentException>(
            () => Produto.CriarSimples(
                "Sabonete",
                new string('a', Produto.DescricaoMaxLength + 1),
                CategoriaAtiva(),
                "UN",
                1m));

        Assert.Equal("nome", nameException.ParamName);
        Assert.Equal("descricao", descriptionException.ParamName);
    }

    [Fact]
    public void AtualizarDados_ValidationFailureDoesNotPartiallyUpdateProduct()
    {
        var produto = Produto.CriarSimples(
            "Sabonete",
            "Lavanda",
            CategoriaAtiva(),
            "UN",
            10m);

        Assert.Throws<ArgumentException>(
            () => produto.AtualizarDados("Sabonete novo", new string('a', Produto.DescricaoMaxLength + 1)));

        Assert.Equal("Sabonete", produto.Nome);
        Assert.Equal("Lavanda", produto.Descricao);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.001)]
    public void CriarVariante_RejectsInvalidSalePrices(decimal preco)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ProdutoVariante.Criar("Padrão", "UN", preco, padrao: true));

        Assert.Equal("precoVenda", exception.ParamName);
    }

    [Fact]
    public void CriarVariante_RejectsPricesOutsideSqlDecimalRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ProdutoVariante.Criar("Padrão", "UN", decimal.MaxValue, padrao: true));

        Assert.Equal("precoVenda", exception.ParamName);
    }

    [Fact]
    public void Criar_RejectsDuplicateNormalizedVariantIdentifications()
    {
        var padrao = ProdutoVariante.Criar("200 g", "UN", 20m, padrao: true);
        var duplicate = ProdutoVariante.Criar(" 200   G ", "UN", 22m);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Produto.Criar("Vela", null, CategoriaAtiva(), [padrao, duplicate]));

        Assert.Contains("identificação", exception.Message);
    }

    [Fact]
    public void AtualizarVariante_UpdatesCommercialFieldsAndRejectsDuplicateIdentification()
    {
        var padrao = ProdutoVariante.Criar("100 g", "UN", 15m, padrao: true);
        var outra = ProdutoVariante.Criar("200 g", "UN", 25m);
        var produto = Produto.Criar("Sabonete", null, CategoriaAtiva(), [padrao, outra]);

        produto.AtualizarVariante(outra.Id, "250 g", " kit ", 29.90m);
        Assert.Equal("250 g", outra.Identificacao);
        Assert.Equal("KIT", outra.UnidadeVenda);
        Assert.Equal(29.90m, outra.PrecoVenda);
        Assert.False(outra.Padrao);
        Assert.True(outra.Ativa);

        Assert.Throws<InvalidOperationException>(
            () => produto.AtualizarVariante(outra.Id, "100 G", "UN", 29.90m));
        Assert.Equal("250 g", outra.Identificacao);
    }

    [Theory]
    [InlineData(" un ", "UN")]
    [InlineData("kit", "KIT")]
    public void CriarVariante_NormalizesSupportedSaleUnits(string unit, string expected)
    {
        var variant = ProdutoVariante.Criar("Padrão", unit, 10m, padrao: true);

        Assert.Equal(expected, variant.UnidadeVenda);
    }

    [Theory]
    [InlineData("UN")]
    [InlineData("KIT")]
    [InlineData("PAR")]
    [InlineData("PCT")]
    [InlineData("CX")]
    [InlineData("CJ")]
    public void CriarVariante_AcceptsAllCommercialSaleUnits(string unit)
    {
        var variant = ProdutoVariante.Criar("Padrão", unit, 10m, padrao: true);

        Assert.Equal(unit, variant.UnidadeVenda);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("G")]
    [InlineData("ML")]
    [InlineData("PCS")]
    public void CriarVariante_RejectsUnsupportedSaleUnits(string? unit)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ProdutoVariante.Criar("Padrão", unit, 10m, padrao: true));

        Assert.Equal("unidadeVenda", exception.ParamName);
    }

    [Fact]
    public void AtualizarVariante_RejectsUnsupportedSaleUnitWithoutChangingExistingData()
    {
        var variant = ProdutoVariante.Criar("Padrão", "UN", 10m, padrao: true);

        var exception = Assert.Throws<ArgumentException>(
            () => variant.AtualizarDados("Padrão", "G", 12m));

        Assert.Equal("unidadeVenda", exception.ParamName);
        Assert.Equal("UN", variant.UnidadeVenda);
        Assert.Equal(10m, variant.PrecoVenda);
    }

    [Fact]
    public void Criar_RejectsMissingOrInconsistentDefaultVariant()
    {
        var active = ProdutoVariante.Criar("200 g", "UN", 20m);
        var inactive = ProdutoVariante.Criar("400 g", "UN", 30m, ativa: false);

        var noDefault = Assert.Throws<ArgumentException>(
            () => Produto.Criar("Vela", null, CategoriaAtiva(), [active, inactive]));
        var inactiveDefaultException = Assert.Throws<ArgumentException>(
            () => ProdutoVariante.Criar("200 g", "UN", 20m, ativa: false, padrao: true));

        Assert.Equal("variantes", noDefault.ParamName);
        Assert.Equal("ativa", inactiveDefaultException.ParamName);
    }

    [Fact]
    public void Criar_RejectsInactiveCategory()
    {
        var categoria = CategoriaAtiva();
        categoria.DefinirAtiva(false);

        var exception = Assert.Throws<ArgumentException>(
            () => Produto.CriarSimples("Vela", null, categoria, "UN", 1m));

        Assert.Equal("categoria", exception.ParamName);
    }

    [Fact]
    public void DesativarDefaultVariant_RequiresAndPromotesAnotherActiveVariantAtomically()
    {
        var categoria = CategoriaAtiva();
        var padrao = ProdutoVariante.Criar("200 g", "UN", 20m, padrao: true);
        var outra = ProdutoVariante.Criar("400 g", "UN", 30m);
        var produto = Produto.Criar("Vela", null, categoria, [padrao, outra]);
        produto.Publicar();

        Assert.Throws<InvalidOperationException>(
            () => produto.DesativarVariante(padrao.Id));
        Assert.True(padrao.Ativa);
        Assert.True(padrao.Padrao);
        Assert.False(outra.Padrao);

        produto.DesativarVariante(padrao.Id, outra.Id);

        Assert.False(padrao.Ativa);
        Assert.False(padrao.Padrao);
        Assert.True(outra.Ativa);
        Assert.True(outra.Padrao);
        Assert.True(produto.Publicado);
        Assert.Single(produto.Variantes, variante => variante.Ativa && variante.Padrao);
    }

    [Fact]
    public void VariantOperations_PreserveOneActiveDefaultAndPublicationRequirements()
    {
        var padrao = ProdutoVariante.Criar("200 g", "UN", 20m, padrao: true);
        var outra = ProdutoVariante.Criar("400 g", "UN", 30m, ativa: false);
        var produto = Produto.Criar("Vela", null, CategoriaAtiva(), [padrao, outra]);

        Assert.Throws<InvalidOperationException>(() => produto.DesativarVariante(padrao.Id));
        produto.AtivarVariante(outra.Id);
        produto.DefinirVariantePadrao(outra.Id);
        produto.Publicar();
        produto.DesativarVariante(padrao.Id);

        Assert.True(produto.Publicado);
        Assert.Single(produto.Variantes, variante => variante.Ativa && variante.Padrao);
        Assert.False(padrao.Ativa);
    }

    [Fact]
    public void Publicar_RejectsInactiveCategoryAndDeactivationNeverRemovesLastActiveVariant()
    {
        var categoria = CategoriaAtiva();
        var padrao = ProdutoVariante.Criar("Padrão", "UN", 10m, padrao: true);
        var produto = Produto.Criar("Sabonete", null, categoria, [padrao]);
        categoria.DefinirAtiva(false);

        Assert.Throws<InvalidOperationException>(() => produto.Publicar());
        Assert.Throws<InvalidOperationException>(() => produto.DesativarVariante(padrao.Id));
        Assert.False(produto.Publicado);
        Assert.True(padrao.Ativa);
        Assert.True(padrao.Padrao);
    }

    private static Categoria CategoriaAtiva() => Categoria.Criar("Categoria", null, 0);

    private static Produto CriarProdutoComDuasVariantes(decimal? pesoPadrao = null)
    {
        var defaultVariant = ProdutoVariante.Criar(
            "Opção principal",
            "UN",
            35m,
            padrao: true,
            pesoLiquidoGramas: pesoPadrao);
        var otherVariant = ProdutoVariante.Criar("Outra opção", "UN", 55m);
        return Produto.Criar("Produto personalizável", null, CategoriaAtiva(), [defaultVariant, otherVariant]);
    }
}
