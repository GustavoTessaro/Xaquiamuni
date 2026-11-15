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
    public void Criar_AllowsMultipleVariantsWithExactlyOneActiveDefault()
    {
        var categoria = Categoria.Criar("Velas", null, 0);
        var padrao = ProdutoVariante.Criar("  200   g ", "G", 25.50m, padrao: true);
        var outra = ProdutoVariante.Criar("400 g", "G", 42m, ativa: false);

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
        var padrao = ProdutoVariante.Criar("200 g", "G", 20m, padrao: true);
        var duplicate = ProdutoVariante.Criar(" 200   G ", "G", 22m);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Produto.Criar("Vela", null, CategoriaAtiva(), [padrao, duplicate]));

        Assert.Contains("identificação", exception.Message);
    }

    [Fact]
    public void AtualizarVariante_UpdatesCommercialFieldsAndRejectsDuplicateIdentification()
    {
        var padrao = ProdutoVariante.Criar("100 g", "G", 15m, padrao: true);
        var outra = ProdutoVariante.Criar("200 g", "G", 25m);
        var produto = Produto.Criar("Sabonete", null, CategoriaAtiva(), [padrao, outra]);

        produto.AtualizarVariante(outra.Id, "250 g", "g", 29.90m);
        Assert.Equal("250 g", outra.Identificacao);
        Assert.Equal("G", outra.UnidadeVenda);
        Assert.Equal(29.90m, outra.PrecoVenda);
        Assert.False(outra.Padrao);
        Assert.True(outra.Ativa);

        Assert.Throws<InvalidOperationException>(
            () => produto.AtualizarVariante(outra.Id, "100 G", "G", 29.90m));
        Assert.Equal("250 g", outra.Identificacao);
    }

    [Fact]
    public void Criar_RejectsMissingOrInconsistentDefaultVariant()
    {
        var active = ProdutoVariante.Criar("200 g", "G", 20m);
        var inactive = ProdutoVariante.Criar("400 g", "G", 30m, ativa: false);

        var noDefault = Assert.Throws<ArgumentException>(
            () => Produto.Criar("Vela", null, CategoriaAtiva(), [active, inactive]));
        var inactiveDefaultException = Assert.Throws<ArgumentException>(
            () => ProdutoVariante.Criar("200 g", "G", 20m, ativa: false, padrao: true));

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
        var padrao = ProdutoVariante.Criar("200 g", "G", 20m, padrao: true);
        var outra = ProdutoVariante.Criar("400 g", "G", 30m);
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
        var padrao = ProdutoVariante.Criar("200 g", "G", 20m, padrao: true);
        var outra = ProdutoVariante.Criar("400 g", "G", 30m, ativa: false);
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
}
