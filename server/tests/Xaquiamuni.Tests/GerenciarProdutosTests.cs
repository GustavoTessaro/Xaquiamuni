using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class GerenciarProdutosTests
{
    [Fact]
    public async Task CadastrarAsync_CreatesSimpleAndMultipleVariantProductsUnpublished()
    {
        var repository = new MemoryProdutoRepository();
        var category = repository.AddCategory("Sabonetes");
        var manager = new GerenciarProdutos(repository);

        var simple = await manager.CadastrarAsync(CreateInput(category.Id));
        var multiple = await manager.CadastrarAsync(CreateInput(
            category.Id,
            extras:
            [
                new ProdutoVarianteInput("200 g", "G", 29.90m),
                new ProdutoVarianteInput("Lavanda", "UN", 35m)
            ]));

        Assert.Equal(ProdutoStatus.Success, simple.Status);
        Assert.Equal(ProdutoStatus.Success, multiple.Status);
        var simpleProduct = Assert.IsType<Produto>(simple.Produto);
        var simpleVariant = Assert.Single(simpleProduct.Variantes);
        Assert.True(simpleVariant.Padrao);
        Assert.True(simpleVariant.Ativa);
        Assert.False(simpleProduct.Publicado);

        var multipleProduct = Assert.IsType<Produto>(multiple.Produto);
        Assert.Equal(3, multipleProduct.Variantes.Count);
        Assert.Single(multipleProduct.Variantes, variante => variante.Padrao && variante.Ativa);
    }

    [Fact]
    public async Task CadastrarAsync_RejectsMissingInactiveCategoriesAndInvalidPrices()
    {
        var repository = new MemoryProdutoRepository();
        var activeCategory = repository.AddCategory("Velas");
        var inactiveCategory = repository.AddCategory("Inativas", ativa: false);
        var manager = new GerenciarProdutos(repository);

        var missing = await manager.CadastrarAsync(CreateInput(Guid.NewGuid()));
        var inactive = await manager.CadastrarAsync(CreateInput(inactiveCategory.Id));
        var invalidPrice = await manager.CadastrarAsync(CreateInput(
            activeCategory.Id,
            defaultVariant: new ProdutoVarianteInput("Padrão", "UN", 0m)));

        Assert.Equal(ProdutoStatus.CategoryNotFound, missing.Status);
        Assert.Equal(ProdutoStatus.CategoryInactive, inactive.Status);
        Assert.Equal(ProdutoStatus.ValidationFailed, invalidPrice.Status);
        Assert.Equal("precoVenda", Assert.Single(invalidPrice.Erros).Campo);
        Assert.Empty(repository.Produtos);
    }

    [Fact]
    public async Task CadastrarAsync_ReportsDuplicateVariantIdentificationAsConflict()
    {
        var repository = new MemoryProdutoRepository();
        var category = repository.AddCategory("Sabonetes");
        var manager = new GerenciarProdutos(repository);

        var result = await manager.CadastrarAsync(CreateInput(
            category.Id,
            extras: [new ProdutoVarianteInput("  PADRÃO ", "UN", 20m)]));

        Assert.Equal(ProdutoStatus.Conflict, result.Status);
        Assert.Contains("identificação", result.Mensagem);
        Assert.Empty(repository.Produtos);
    }

    [Fact]
    public async Task EditAndVariantOperations_UpdateValuesAndRejectInvalidTransitions()
    {
        var repository = new MemoryProdutoRepository();
        var category = repository.AddCategory("Categoria inicial");
        var nextCategory = repository.AddCategory("Categoria nova");
        var manager = new GerenciarProdutos(repository);
        var created = await manager.CadastrarAsync(CreateInput(category.Id));
        var product = Assert.IsType<Produto>(created.Produto);
        var originalVariant = Assert.Single(product.Variantes);

        var edited = await manager.EditarAsync(
            product.Id,
            new ProdutoEdicaoInput("Sabonete novo", "Descrição", nextCategory.Id));
        var added = await manager.AdicionarVarianteAsync(
            product.Id,
            new ProdutoVarianteInput("250 g", "g", 30m));
        var newVariant = Assert.Single(product.Variantes, variante => !variante.Padrao);
        var variantEdited = await manager.EditarVarianteAsync(
            product.Id,
            newVariant.Id,
            new ProdutoVarianteInput("300 g", "g", 34.50m));
        var duplicate = await manager.EditarVarianteAsync(
            product.Id,
            newVariant.Id,
            new ProdutoVarianteInput(originalVariant.Identificacao, "UN", 34.50m));
        var deactivatedLastVariant = await manager.AlterarStatusVarianteAsync(
            product.Id,
            originalVariant.Id,
            false,
            null);

        Assert.Equal(ProdutoStatus.Success, edited.Status);
        Assert.Equal(ProdutoStatus.Success, added.Status);
        Assert.Equal(ProdutoStatus.Success, variantEdited.Status);
        Assert.Equal(ProdutoStatus.Conflict, duplicate.Status);
        Assert.Equal(ProdutoStatus.Conflict, deactivatedLastVariant.Status);
        Assert.Equal("Sabonete novo", product.Nome);
        Assert.Equal(nextCategory.Id, product.CategoriaId);
        Assert.Equal("300 g", newVariant.Identificacao);
        Assert.Equal(34.50m, newVariant.PrecoVenda);
        Assert.True(originalVariant.Ativa);
    }

    [Fact]
    public async Task PublicationRequiresActiveCategoryAndCanBeReversed()
    {
        var repository = new MemoryProdutoRepository();
        var category = repository.AddCategory("Velas");
        var manager = new GerenciarProdutos(repository);
        var created = await manager.CadastrarAsync(CreateInput(category.Id));
        var product = Assert.IsType<Produto>(created.Produto);

        category.DefinirAtiva(false);
        var rejected = await manager.AlterarPublicacaoAsync(product.Id, true);
        Assert.Equal(ProdutoStatus.CategoryInactive, rejected.Status);

        category.DefinirAtiva(true);
        var published = await manager.AlterarPublicacaoAsync(product.Id, true);
        var unpublished = await manager.AlterarPublicacaoAsync(product.Id, false);

        Assert.Equal(ProdutoStatus.Success, published.Status);
        Assert.Equal(ProdutoStatus.Success, unpublished.Status);
        Assert.False(product.Publicado);
    }

    [Fact]
    public async Task ListarAsync_AppliesNameCategoryAndPublicationFilters()
    {
        var repository = new MemoryProdutoRepository();
        var soaps = repository.AddCategory("Sabonetes");
        var candles = repository.AddCategory("Velas");
        var manager = new GerenciarProdutos(repository);
        var soapResult = await manager.CadastrarAsync(CreateInput(soaps.Id, "Sabonete de Lavanda"));
        var candleResult = await manager.CadastrarAsync(CreateInput(candles.Id, "Vela de Lavanda"));
        var soap = Assert.IsType<Produto>(soapResult.Produto);
        var candle = Assert.IsType<Produto>(candleResult.Produto);
        soap.Publicar();

        var filtered = await manager.ListarAsync(new ProdutoFiltro("LAVANDA", soaps.Id, true));

        Assert.Single(filtered);
        Assert.Equal(soap.Id, filtered[0].Id);
        Assert.DoesNotContain(candle, filtered);
    }

    private static ProdutoCadastroInput CreateInput(
        Guid categoryId,
        string name = "Produto artesanal",
        ProdutoVarianteInput? defaultVariant = null,
        IReadOnlyList<ProdutoVarianteInput?>? extras = null) =>
        new(
            name,
            null,
            categoryId,
            defaultVariant ?? new ProdutoVarianteInput("Padrão", "UN", 20m),
            extras);

    private sealed class MemoryProdutoRepository : IProdutoRepository
    {
        private readonly List<Categoria> _categories = [];

        public List<Produto> Produtos { get; } = [];

        public Categoria AddCategory(string name, bool ativa = true)
        {
            var category = Categoria.Criar(name, null, 0);
            if (!ativa)
            {
                category.DefinirAtiva(false);
            }

            _categories.Add(category);
            return category;
        }

        public Task<IReadOnlyList<Produto>> ListarAsync(
            ProdutoFiltro filtro,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Produto> query = Produtos;
            if (!string.IsNullOrWhiteSpace(filtro.Busca))
            {
                query = query.Where(produto =>
                    produto.Nome.Contains(filtro.Busca, StringComparison.OrdinalIgnoreCase));
            }

            if (filtro.CategoriaId is not null)
            {
                query = query.Where(produto => produto.CategoriaId == filtro.CategoriaId.Value);
            }

            if (filtro.Publicado is not null)
            {
                query = query.Where(produto => produto.Publicado == filtro.Publicado.Value);
            }

            return Task.FromResult<IReadOnlyList<Produto>>(query.ToArray());
        }

        public Task<Produto?> ConsultarAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Produtos.SingleOrDefault(produto => produto.Id == id));

        public Task<ProdutoResult> ExecutarMutacaoAsync(
            Func<IProdutoMutationSession, CancellationToken, Task<ProdutoResult>> operacao,
            CancellationToken cancellationToken = default) =>
            operacao(new MemoryMutationSession(_categories, Produtos), cancellationToken);
    }

    private sealed class MemoryMutationSession(
        IReadOnlyCollection<Categoria> categories,
        ICollection<Produto> products) : IProdutoMutationSession
    {
        public Task<Categoria?> BuscarCategoriaAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(categories.SingleOrDefault(categoria => categoria.Id == id));

        public Task<Produto?> BuscarProdutoAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(products.SingleOrDefault(produto => produto.Id == id));

        public void Adicionar(Produto produto) => products.Add(produto);
    }
}
