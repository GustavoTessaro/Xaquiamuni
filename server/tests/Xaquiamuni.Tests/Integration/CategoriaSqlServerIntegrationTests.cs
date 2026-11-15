using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class CategoriaSqlServerIntegrationTests
{
    private const string AddCategoriasMigration = "20261115000009_AddCategorias";

    [SqlServerIntegrationFact]
    public async Task Migration_CreatesCategorySchemaAndRepositoryListsAllCategoriesDeterministically()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(AddCategoriasMigration, appliedMigrations);

        var repository = new EfCategoriaRepository(context);
        var first = Categoria.Criar("Velas", null, 2);
        var second = Categoria.Criar("Sabonetes", null, 1);
        var third = Categoria.Criar("Aromatizadores", null, 1);
        second.DefinirAtiva(false);

        Assert.True(await repository.AdicionarAsync(first));
        Assert.True(await repository.AdicionarAsync(second));
        Assert.True(await repository.AdicionarAsync(third));

        var categorias = await repository.ListarAsync();
        var expectedIds = new[] { first, second, third }
            .OrderBy(categoria => categoria.OrdemExibicao)
            .ThenBy(categoria => categoria.Id)
            .Select(categoria => categoria.Id);

        Assert.Equal(expectedIds, categorias.Select(categoria => categoria.Id));
        Assert.Contains(categorias, categoria => !categoria.Ativa);
        Assert.Equal("Categorias", context.Model.FindEntityType(typeof(Categoria))?.GetTableName());
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentDuplicateInserts_EnforceUniqueIndexWithoutExposingSqlException()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var first = Categoria.Criar("  Velas   Aromáticas ", null, 0);
        var second = Categoria.Criar("VELAS AROMÁTICAS", null, 1);

        var results = await Task.WhenAll(
            TryAddInSeparateContextAsync(database, first),
            TryAddInSeparateContextAsync(database, second));

        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);

        await using var verificationContext = database.CreateVerificationContext();
        Assert.Equal(1, await verificationContext.Categorias.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task UseCases_PersistCreateEditAndStatusChangesAndRejectDuplicateNames()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var service = new GerenciarCategorias(new EfCategoriaRepository(context));

        var created = await service.CadastrarAsync(new CategoriaInput("Velas Aromáticas", "Feitas à mão", 0));
        var categoryId = Assert.IsType<Categoria>(created.Categoria).Id;
        var duplicate = await service.CadastrarAsync(new CategoriaInput("  VELAS   AROMÁTICAS ", null, 2));
        var edited = await service.EditarAsync(
            categoryId,
            new CategoriaInput("Velas Artesanais", "Linha natural", 4));
        var deactivated = await service.AlterarStatusAsync(categoryId, false);

        Assert.Equal(CategoriaStatus.Success, created.Status);
        Assert.Equal(CategoriaStatus.NameAlreadyExists, duplicate.Status);
        Assert.Equal(CategoriaStatus.Success, edited.Status);
        Assert.Equal(CategoriaStatus.Success, deactivated.Status);
        Assert.False(deactivated.Categoria?.Ativa);

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Categorias.SingleAsync();
        Assert.Equal("Velas Artesanais", persisted.Nome);
        Assert.Equal("Linha natural", persisted.Descricao);
        Assert.Equal(4, persisted.OrdemExibicao);
        Assert.False(persisted.Ativa);

        var activated = await service.AlterarStatusAsync(categoryId, true);
        Assert.Equal(CategoriaStatus.Success, activated.Status);
        Assert.True(activated.Categoria?.Ativa);
    }

    private static async Task<bool> TryAddInSeparateContextAsync(
        SqlServerTestDatabase database,
        Categoria categoria)
    {
        await using var context = database.CreateVerificationContext();
        return await new EfCategoriaRepository(context).AdicionarAsync(categoria);
    }
}
