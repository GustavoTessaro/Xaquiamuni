using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
}
