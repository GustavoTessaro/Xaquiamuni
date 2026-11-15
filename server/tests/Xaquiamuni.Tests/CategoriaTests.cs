using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class CategoriaTests
{
    [Fact]
    public void Criar_NormalizesNameAndDescriptionAndStartsActive()
    {
        var categoria = Categoria.Criar("  Velas   Aromáticas  ", "  Feitas \t à mão ", 2);

        Assert.Equal("Velas Aromáticas", categoria.Nome);
        Assert.Equal("VELAS AROMÁTICAS", categoria.NomeNormalizado);
        Assert.Equal("Feitas à mão", categoria.Descricao);
        Assert.True(categoria.Ativa);
        Assert.Equal(2, categoria.OrdemExibicao);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_RejectsMissingName(string? nome)
    {
        var exception = Assert.Throws<ArgumentException>(() => Categoria.Criar(nome, null, 0));

        Assert.Equal("nome", exception.ParamName);
    }

    [Fact]
    public void Criar_RejectsNameAndDescriptionBeyondTheirLimits()
    {
        var nameException = Assert.Throws<ArgumentException>(
            () => Categoria.Criar(new string('a', Categoria.NomeMaxLength + 1), null, 0));
        var descriptionException = Assert.Throws<ArgumentException>(
            () => Categoria.Criar("Sabonetes", new string('a', Categoria.DescricaoMaxLength + 1), 0));

        Assert.Equal("nome", nameException.ParamName);
        Assert.Equal("descricao", descriptionException.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Criar_RejectsMissingOrNegativeDisplayOrder(int? ordem)
    {
        var exception = Assert.Throws<ArgumentException>(() => Categoria.Criar("Sabonetes", null, ordem));

        Assert.Equal("ordemExibicao", exception.ParamName);
    }

    [Fact]
    public void UpdateAndStatusChanges_ChangeOnlyRequestedValues()
    {
        var categoria = Categoria.Criar("Sabonetes", "Artesanais", 1);

        categoria.AtualizarDados("  Sabonetes  Naturais ", null, 3);
        categoria.DefinirAtiva(false);

        Assert.Equal("Sabonetes Naturais", categoria.Nome);
        Assert.Equal("SABONETES NATURAIS", categoria.NomeNormalizado);
        Assert.Null(categoria.Descricao);
        Assert.Equal(3, categoria.OrdemExibicao);
        Assert.False(categoria.Ativa);
    }
}

public sealed class GerenciarCategoriasTests
{
    [Fact]
    public async Task CadastrarAsync_RejectsDuplicateNormalizedName()
    {
        var repository = new InMemoryCategoriaRepository();
        var service = new GerenciarCategorias(repository);

        var first = await service.CadastrarAsync(new CategoriaInput(" Sabonetes  Naturais ", null, 0));
        var duplicate = await service.CadastrarAsync(new CategoriaInput("SABONETES NATURAIS", null, 1));

        Assert.Equal(CategoriaStatus.Success, first.Status);
        Assert.Equal(CategoriaStatus.NameAlreadyExists, duplicate.Status);
        Assert.Single(repository.Categorias);
    }

    [Fact]
    public async Task EditAndStatusOperations_UpdateExistingCategoryAndReportMissingIds()
    {
        var repository = new InMemoryCategoriaRepository();
        var service = new GerenciarCategorias(repository);
        var created = await service.CadastrarAsync(new CategoriaInput("Velas", "Aromáticas", 0));
        var id = Assert.IsType<Categoria>(created.Categoria).Id;

        var edited = await service.EditarAsync(id, new CategoriaInput("Velas Artesanais", null, 4));
        var deactivated = await service.AlterarStatusAsync(id, false);
        Assert.False(deactivated.Categoria?.Ativa);
        var activated = await service.AlterarStatusAsync(id, true);
        var missingUpdate = await service.EditarAsync(Guid.NewGuid(), new CategoriaInput("Outra", null, 0));
        var missingStatus = await service.AlterarStatusAsync(Guid.NewGuid(), false);

        Assert.Equal(CategoriaStatus.Success, edited.Status);
        Assert.Equal("Velas Artesanais", edited.Categoria?.Nome);
        Assert.Equal(CategoriaStatus.Success, deactivated.Status);
        Assert.Equal(CategoriaStatus.Success, activated.Status);
        Assert.True(activated.Categoria?.Ativa);
        Assert.Equal(CategoriaStatus.NotFound, missingUpdate.Status);
        Assert.Equal(CategoriaStatus.NotFound, missingStatus.Status);
    }

    [Fact]
    public async Task CadastrarAsync_MapsInvalidInputToFieldError()
    {
        var service = new GerenciarCategorias(new InMemoryCategoriaRepository());

        var result = await service.CadastrarAsync(new CategoriaInput(" ", null, -1));

        Assert.Equal(CategoriaStatus.ValidationFailed, result.Status);
        var error = Assert.Single(result.Erros);
        Assert.Equal("nome", error.Campo);
    }

    private sealed class InMemoryCategoriaRepository : ICategoriaRepository
    {
        public List<Categoria> Categorias { get; } = [];

        public Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Categoria>>(Categorias
                .OrderBy(categoria => categoria.OrdemExibicao)
                .ThenBy(categoria => categoria.Id)
                .ToArray());

        public Task<Categoria?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Categorias.SingleOrDefault(categoria => categoria.Id == id));

        public Task<bool> ExisteNomeNormalizadoAsync(
            string nomeNormalizado,
            Guid? excetoId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Categorias.Any(categoria =>
                categoria.NomeNormalizado == nomeNormalizado
                && categoria.Id != excetoId));

        public Task<bool> AdicionarAsync(Categoria categoria, CancellationToken cancellationToken = default)
        {
            Categorias.Add(categoria);
            return Task.FromResult(true);
        }

        public Task<bool> AtualizarAsync(Categoria categoria, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
