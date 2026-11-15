using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class ProdutoSqlServerIntegrationTests
{
    private const string AddProdutosEVariantesMigration = "20261115021210_AddProdutosEVariantes";

    [SqlServerIntegrationFact]
    public async Task Migration_PersistsProductsVariantsAndCategoryRelationship()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(AddProdutosEVariantesMigration, appliedMigrations);

        var categoria = Categoria.Criar("Sabonetes", null, 0);
        context.Categorias.Add(categoria);
        await context.SaveChangesAsync();

        var produto = Produto.Criar(
            "Sabonete artesanal",
            "Produzido à mão",
            categoria,
            [
                ProdutoVariante.Criar("Padrão 100 g", "G", 18.50m, padrao: true),
                ProdutoVariante.Criar("200 g", "G", 30m)
            ]);
        context.Produtos.Add(produto);
        await context.SaveChangesAsync();

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Categoria)
            .Include(item => item.Variantes)
            .SingleAsync(item => item.Id == produto.Id);

        Assert.Equal("Produtos", verificationContext.Model.FindEntityType(typeof(Produto))?.GetTableName());
        Assert.Equal(
            "ProdutoVariantes",
            verificationContext.Model.FindEntityType(typeof(ProdutoVariante))?.GetTableName());
        Assert.Equal(categoria.Id, persisted.CategoriaId);
        Assert.Equal("Sabonetes", persisted.Categoria.Nome);
        Assert.Equal(2, persisted.Variantes.Count);
        Assert.Single(persisted.Variantes, variant => variant.Padrao && variant.Ativa);
        Assert.Equal(18.50m, persisted.Variantes.Single(variant => variant.Padrao).PrecoVenda);
    }

    [SqlServerIntegrationFact]
    public async Task Schema_EnforcesVariantConstraintsAndPreservesHistoricalForeignKeys()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid categoryId;
        Guid productId;

        await using (var context = database.CreateVerificationContext())
        {
            var categoria = Categoria.Criar("Velas", null, 0);
            context.Categorias.Add(categoria);
            await context.SaveChangesAsync();

            var produto = Produto.CriarSimples("Vela artesanal", null, categoria, "UN", 25m);
            context.Produtos.Add(produto);
            await context.SaveChangesAsync();

            categoryId = categoria.Id;
            productId = produto.Id;
            Assert.Single(produto.Variantes);
        }

        await using var constraintContext = database.CreateVerificationContext();
        var duplicateIdentification = InsertVariantAsync(
            constraintContext,
            productId,
            Guid.NewGuid(),
            "Padrão",
            "PADRÃO",
            "UN",
            26m,
            active: true,
            isDefault: false);
        await Assert.ThrowsAsync<SqlException>(() => duplicateIdentification);

        var nonPositivePrice = InsertVariantAsync(
            constraintContext,
            productId,
            Guid.NewGuid(),
            "Amostra",
            "AMOSTRA",
            "UN",
            0m,
            active: true,
            isDefault: false);
        await Assert.ThrowsAsync<SqlException>(() => nonPositivePrice);

        var inactiveDefault = InsertVariantAsync(
            constraintContext,
            productId,
            Guid.NewGuid(),
            "Inativa",
            "INATIVA",
            "UN",
            1m,
            active: false,
            isDefault: true);
        await Assert.ThrowsAsync<SqlException>(() => inactiveDefault);

        var secondDefault = InsertVariantAsync(
            constraintContext,
            productId,
            Guid.NewGuid(),
            "Outra padrão",
            "OUTRA PADRÃO",
            "UN",
            1m,
            active: true,
            isDefault: true);
        await Assert.ThrowsAsync<SqlException>(() => secondDefault);

        var deleteProduct = constraintContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [Produtos] WHERE [Id] = {productId}");
        await Assert.ThrowsAsync<SqlException>(() => deleteProduct);

        var deleteCategory = constraintContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [Categorias] WHERE [Id] = {categoryId}");
        await Assert.ThrowsAsync<SqlException>(() => deleteCategory);
    }

    [SqlServerIntegrationFact]
    public async Task AdminProductCases_PersistVariantsFiltersPublicationAndCategoryDeactivation()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(AddProdutosEVariantesMigration, appliedMigrations);

        var category = Categoria.Criar("Sabonetes", null, 0);
        var otherCategory = Categoria.Criar("Velas", null, 1);
        context.Categorias.AddRange(category, otherCategory);
        await context.SaveChangesAsync();

        var manager = database.Services.GetRequiredService<IGerenciarProdutos>();
        var created = await manager.CadastrarAsync(
            new ProdutoCadastroInput(
                "Sabonete de Lavanda",
                "Produção artesanal",
                category.Id,
                new ProdutoVarianteInput("100 g", "G", 20m),
                [new ProdutoVarianteInput("200 g", "G", 35m)]));
        Assert.Equal(ProdutoStatus.Success, created.Status);
        var product = Assert.IsType<Produto>(created.Produto);
        var originalDefault = Assert.Single(product.Variantes, variant => variant.Padrao);
        var otherVariant = Assert.Single(product.Variantes, variant => !variant.Padrao);

        var edited = await manager.EditarAsync(
            product.Id,
            new ProdutoEdicaoInput("Sabonete Natural", "Lavanda", otherCategory.Id));
        var variantEdited = await manager.EditarVarianteAsync(
            product.Id,
            otherVariant.Id,
            new ProdutoVarianteInput("250 g", "G", 39.90m));
        var switched = await manager.DefinirVariantePadraoAsync(product.Id, otherVariant.Id);
        var deactivated = await manager.AlterarStatusVarianteAsync(
            product.Id,
            originalDefault.Id,
            false,
            null);
        var published = await manager.AlterarPublicacaoAsync(product.Id, true);
        var publishedList = await manager.ListarAsync(
            new ProdutoFiltro("NATURAL", otherCategory.Id, Publicado: true));

        Assert.Equal(ProdutoStatus.Success, edited.Status);
        Assert.Equal(ProdutoStatus.Success, variantEdited.Status);
        Assert.Equal(ProdutoStatus.Success, switched.Status);
        Assert.Equal(ProdutoStatus.Success, deactivated.Status);
        Assert.Equal(ProdutoStatus.Success, published.Status);
        Assert.Single(publishedList);
        Assert.Equal(product.Id, publishedList[0].Id);

        var categoryResult = await database.Services.GetRequiredService<IGerenciarCategorias>()
            .AlterarStatusAsync(otherCategory.Id, false);
        Assert.Equal(CategoriaStatus.Success, categoryResult.Status);

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Categoria)
            .Include(item => item.Variantes)
            .SingleAsync(item => item.Id == product.Id);
        Assert.Equal("Sabonete Natural", persisted.Nome);
        Assert.Equal("Lavanda", persisted.Descricao);
        Assert.False(persisted.Categoria.Ativa);
        Assert.False(persisted.Publicado);
        Assert.False(persisted.Variantes.Single(variant => variant.Id == originalDefault.Id).Ativa);
        Assert.True(persisted.Variantes.Single(variant => variant.Id == otherVariant.Id).Padrao);
        Assert.Equal(39.90m, persisted.Variantes.Single(variant => variant.Id == otherVariant.Id).PrecoVenda);

        var cannotPublishInactiveCategory = await manager.AlterarPublicacaoAsync(product.Id, true);
        Assert.Equal(ProdutoStatus.CategoryInactive, cannotPublishInactiveCategory.Status);
    }

    [SqlServerIntegrationFact]
    public async Task DefaultVariantSwitch_SaveFailureRollsBackTheDemotionAndPromotion()
    {
        var saveInterceptor = new FailNextSaveInterceptor();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: saveInterceptor);
        var context = database.Services.GetRequiredService<XaquiamuniDbContext>();
        var category = Categoria.Criar("Velas", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var manager = database.Services.GetRequiredService<IGerenciarProdutos>();
        var created = await manager.CadastrarAsync(
            new ProdutoCadastroInput(
                "Vela artesanal",
                null,
                category.Id,
                new ProdutoVarianteInput("Padrão", "UN", 25m),
                [new ProdutoVarianteInput("200 g", "G", 38m)]));
        var product = Assert.IsType<Produto>(created.Produto);
        var originalDefault = Assert.Single(product.Variantes, variant => variant.Padrao);
        var replacement = Assert.Single(product.Variantes, variant => !variant.Padrao);

        saveInterceptor.FailNextSave = true;
        await Assert.ThrowsAsync<DbUpdateException>(
            () => manager.AlterarStatusVarianteAsync(
                product.Id,
                originalDefault.Id,
                false,
                replacement.Id));

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Variantes)
            .SingleAsync(item => item.Id == product.Id);
        Assert.True(persisted.Variantes.Single(variant => variant.Id == originalDefault.Id).Ativa);
        Assert.True(persisted.Variantes.Single(variant => variant.Id == originalDefault.Id).Padrao);
        Assert.False(persisted.Variantes.Single(variant => variant.Id == replacement.Id).Padrao);
    }

    private static Task<int> InsertVariantAsync(
        XaquiamuniDbContext context,
        Guid productId,
        Guid id,
        string identification,
        string normalizedIdentification,
        string unit,
        decimal price,
        bool active,
        bool isDefault) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoVariantes]
                ([Id], [ProdutoId], [Identificacao], [IdentificacaoNormalizada], [UnidadeVenda],
                 [PrecoVenda], [Ativa], [Padrao])
            VALUES
                ({id}, {productId}, {identification}, {normalizedIdentification}, {unit},
                 {price}, {active}, {isDefault});
            """);

    private sealed class FailNextSaveInterceptor : SaveChangesInterceptor
    {
        public bool FailNextSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new DbUpdateException("Injected product persistence failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
