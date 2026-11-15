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
    private const string AddProductCharacteristicsMigrationSuffix =
        "_AddProdutoCaracteristicasEMedidasFisicas";
    private const string AddProductImagesMigrationSuffix = "_AddProdutoImagens";

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
                ProdutoVariante.Criar("Padrão 100 g", "UN", 18.50m, padrao: true),
                ProdutoVariante.Criar("200 g", "UN", 30m)
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
    public async Task ProductImages_PersistReloadReorderAndEnforceScopedLinksAndUniqueIndexes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Velas", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var mainVariant = ProdutoVariante.Criar("100 g", "UN", 35m, padrao: true);
        var otherVariant = ProdutoVariante.Criar("200 g", "KIT", 55m);
        var product = Produto.Criar("Vela aromática", null, category, [mainVariant, otherVariant]);
        var mainImage = AdicionarImagem(product, "products/vela/main.jpg", "foto principal.jpg");
        var secondaryImage = AdicionarImagem(product, "products/vela/secondary.jpg", "detalhe.webp");
        product.AssociarImagemAVariante(mainImage.Id, mainVariant.Id);
        product.AssociarImagemAVariante(mainImage.Id, otherVariant.Id);
        product.AssociarImagemAVariante(secondaryImage.Id, otherVariant.Id);
        context.Produtos.Add(product);
        await context.SaveChangesAsync();

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Variantes)
            .Include(item => item.Imagens)
                .ThenInclude(image => image.Variantes)
                    .ThenInclude(association => association.Variante)
            .SingleAsync(item => item.Id == product.Id);
        Assert.Equal(2, persisted.Imagens.Count);
        Assert.Single(persisted.Imagens, image => image.Principal);
        Assert.Equal(
            new[] { mainImage.Id, secondaryImage.Id },
            persisted.Imagens.OrderBy(image => image.OrdemExibicao).Select(image => image.Id));
        var persistedMainImage = persisted.Imagens.Single(image => image.Id == mainImage.Id);
        Assert.Equal("products/vela/main.jpg", persistedMainImage.ChaveArmazenamento);
        Assert.Equal("foto principal.jpg", persistedMainImage.NomeOriginal);
        Assert.Equal(2, persistedMainImage.Variantes.Count);
        Assert.Contains(
            persistedMainImage.Variantes,
            association => association.Variante.Id == mainVariant.Id);

        var repository = new EfProdutoRepository(verificationContext);
        var reordered = await repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var loaded = await session.BuscarProdutoAsync(product.Id, token);
            Assert.NotNull(loaded);
            loaded.DefinirImagemPrincipal(secondaryImage.Id);
            loaded.ReordenarImagens([secondaryImage.Id, mainImage.Id]);
            return ProdutoResult.Success(loaded);
        });
        Assert.Equal(ProdutoStatus.Success, reordered.Status);

        await using var constraintContext = database.CreateVerificationContext();
        var changed = await constraintContext.Produtos
            .Include(item => item.Imagens)
            .SingleAsync(item => item.Id == product.Id);
        Assert.Equal(
            new[] { secondaryImage.Id, mainImage.Id },
            changed.Imagens.OrderBy(image => image.OrdemExibicao).Select(image => image.Id));
        Assert.True(changed.Imagens.Single(image => image.Id == secondaryImage.Id).Principal);

        var removedMain = await repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var loaded = await session.BuscarProdutoAsync(product.Id, token);
            Assert.NotNull(loaded);
            loaded.RemoverImagem(secondaryImage.Id);
            return ProdutoResult.Success(loaded);
        });
        Assert.Equal(ProdutoStatus.Success, removedMain.Status);

        await using var removalVerificationContext = database.CreateVerificationContext();
        var afterRemoval = await removalVerificationContext.Produtos
            .Include(item => item.Imagens)
                .ThenInclude(image => image.Variantes)
                    .ThenInclude(association => association.Variante)
            .SingleAsync(item => item.Id == product.Id);
        var promotedImage = Assert.Single(afterRemoval.Imagens);
        Assert.Equal(mainImage.Id, promotedImage.Id);
        Assert.True(promotedImage.Principal);
        Assert.Single(promotedImage.Variantes, association =>
            association.ProdutoVarianteId == mainVariant.Id);

        var otherProduct = Produto.CriarSimples("Outro produto", null, category, "UN", 10m);
        context.Produtos.Add(otherProduct);
        await context.SaveChangesAsync();
        var otherProductVariantId = Assert.Single(otherProduct.Variantes).Id;
        var crossProductLink = removalVerificationContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoImagemVariantes] ([ProdutoImagemId], [ProdutoVarianteId], [ProdutoId])
            VALUES ({mainImage.Id}, {otherProductVariantId}, {product.Id});
            """);
        await Assert.ThrowsAsync<SqlException>(() => crossProductLink);

        var duplicateAssociation = removalVerificationContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoImagemVariantes] ([ProdutoImagemId], [ProdutoVarianteId], [ProdutoId])
            VALUES ({mainImage.Id}, {mainVariant.Id}, {product.Id});
            """);
        await Assert.ThrowsAsync<SqlException>(() => duplicateAssociation);

        var duplicateStorageKey = removalVerificationContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoImagens]
                ([Id], [ProdutoId], [ChaveArmazenamento], [NomeOriginal], [TipoConteudo],
                 [TamanhoBytes], [LarguraPixels], [AlturaPixels], [OrdemExibicao], [SlotGaleria],
                 [Principal], [CriadoEmUtc])
            VALUES
                ({Guid.NewGuid()}, {otherProduct.Id}, {"products/vela/main.jpg"}, {"duplicada.jpg"},
                 {"image/jpeg"}, {1024L}, {100}, {100}, {1}, {1}, {true}, {DateTimeOffset.UtcNow});
            """);
        await Assert.ThrowsAsync<SqlException>(() => duplicateStorageKey);

        var duplicateMainImage = removalVerificationContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoImagens]
                ([Id], [ProdutoId], [ChaveArmazenamento], [NomeOriginal], [TipoConteudo],
                 [TamanhoBytes], [LarguraPixels], [AlturaPixels], [OrdemExibicao], [SlotGaleria],
                 [Principal], [CriadoEmUtc])
            VALUES
                ({Guid.NewGuid()}, {product.Id}, {"products/vela/duplicate-main.jpg"}, {"duplicada.jpg"},
                 {"image/jpeg"}, {1024L}, {100}, {100}, {3}, {3}, {true}, {DateTimeOffset.UtcNow});
            """);
        await Assert.ThrowsAsync<SqlException>(() => duplicateMainImage);

        var outOfRangeSlot = removalVerificationContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoImagens]
                ([Id], [ProdutoId], [ChaveArmazenamento], [NomeOriginal], [TipoConteudo],
                 [TamanhoBytes], [LarguraPixels], [AlturaPixels], [OrdemExibicao], [SlotGaleria],
                 [Principal], [CriadoEmUtc])
            VALUES
                ({Guid.NewGuid()}, {otherProduct.Id}, {"products/other/invalid-slot.jpg"}, {"invalid.jpg"},
                 {"image/jpeg"}, {1024L}, {100}, {100}, {1}, {6}, {true}, {DateTimeOffset.UtcNow});
            """);
        await Assert.ThrowsAsync<SqlException>(() => outOfRangeSlot);
    }

    [SqlServerIntegrationFact]
    public async Task ProductImageGallerySlot_RejectsConcurrentSixthImage()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Sabonetes", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();
        var product = Produto.CriarSimples("Sabonete", null, category, "UN", 15m);
        for (var index = 1; index <= 4; index++)
        {
            AdicionarImagem(product, $"products/sabao/{index}.jpg", $"foto-{index}.jpg");
        }

        context.Produtos.Add(product);
        await context.SaveChangesAsync();

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstInsert = InsertConcurrentImageSlotAsync(
            database,
            start.Task,
            product.Id,
            "products/sabao/concurrent-a.jpg");
        var secondInsert = InsertConcurrentImageSlotAsync(
            database,
            start.Task,
            product.Id,
            "products/sabao/concurrent-b.jpg");
        start.SetResult(true);

        var outcomes = await Task.WhenAll(firstInsert, secondInsert);

        Assert.Single(outcomes, outcome => outcome is null);
        var conflict = Assert.IsType<SqlException>(Assert.Single(outcomes, outcome => outcome is not null));
        Assert.True(conflict.Number is 2601 or 2627);
        await using var verificationContext = database.CreateVerificationContext();
        Assert.Equal(5, await verificationContext.ProdutoImagens.CountAsync(image => image.ProdutoId == product.Id));
    }

    [SqlServerIntegrationFact]
    public async Task Migration_PreservesExistingVariantsWithoutInventingCharacteristicsOrMeasurements()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: AddProdutosEVariantesMigration);
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Sabonetes", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var productId = Guid.NewGuid();
        var defaultVariantId = Guid.NewGuid();
        var otherVariantId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Produtos] ([Id], [Nome], [Descricao], [CategoriaId], [Publicado])
            VALUES ({productId}, {"Sabonete para Descarrego"}, {"Cadastro legado"}, {category.Id}, {true});

            INSERT INTO [ProdutoVariantes]
                ([Id], [ProdutoId], [Identificacao], [IdentificacaoNormalizada], [UnidadeVenda],
                 [PrecoVenda], [Ativa], [Padrao])
            VALUES
                ({defaultVariantId}, {productId}, {"Padrão 100 g"}, {"PADRÃO 100 G"}, {"G"},
                 {15m}, {true}, {true}),
                ({otherVariantId}, {productId}, {"200 g Editado"}, {"200 G EDITADO"}, {"KIT"},
                 {55m}, {false}, {false});
            """);

        await context.Database.MigrateAsync();
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(
            appliedMigrations,
            migration => migration.EndsWith(
                AddProductCharacteristicsMigrationSuffix,
                StringComparison.Ordinal));
        Assert.Contains(
            appliedMigrations,
            migration => migration.EndsWith(AddProductImagesMigrationSuffix, StringComparison.Ordinal));

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Categoria)
            .Include(item => item.Variantes)
                .ThenInclude(variant => variant.CaracteristicaValores)
            .Include(item => item.Caracteristicas)
            .Include(item => item.Imagens)
            .SingleAsync(item => item.Id == productId);

        Assert.Equal(productId, persisted.Id);
        Assert.Equal("Sabonete para Descarrego", persisted.Nome);
        Assert.Equal("Cadastro legado", persisted.Descricao);
        Assert.Equal(category.Id, persisted.CategoriaId);
        Assert.Equal("Sabonetes", persisted.Categoria.Nome);
        Assert.True(persisted.Categoria.Ativa);
        Assert.True(persisted.Publicado);
        Assert.Empty(persisted.Imagens);
        Assert.Empty(persisted.Caracteristicas);
        Assert.Equal(2, persisted.Variantes.Count);
        var defaultVariant = persisted.Variantes.Single(item => item.Id == defaultVariantId);
        var otherVariant = persisted.Variantes.Single(item => item.Id == otherVariantId);
        Assert.Equal("Padrão 100 g", defaultVariant.Identificacao);
        Assert.Equal("G", defaultVariant.UnidadeVenda);
        Assert.Equal(15m, defaultVariant.PrecoVenda);
        Assert.True(defaultVariant.Ativa);
        Assert.True(defaultVariant.Padrao);
        Assert.Equal("200 g Editado", otherVariant.Identificacao);
        Assert.Equal("KIT", otherVariant.UnidadeVenda);
        Assert.Equal(55m, otherVariant.PrecoVenda);
        Assert.False(otherVariant.Ativa);
        Assert.False(otherVariant.Padrao);
        Assert.Null(defaultVariant.PesoLiquidoGramas);
        Assert.Null(defaultVariant.VolumeLiquidoMililitros);
        Assert.Empty(defaultVariant.CaracteristicaValores);
        Assert.Empty(otherVariant.CaracteristicaValores);
    }

    [SqlServerIntegrationFact]
    public async Task SimpleProductMeasurements_PersistAndReloadAsCanonicalValues()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Sabonetes", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var product = Produto.CriarSimples(
            "Sabonete com medidas",
            null,
            category,
            "UN",
            18m,
            pesoLiquidoGramas: 123.456m,
            volumeLiquidoMililitros: 78.9m);
        context.Produtos.Add(product);
        await context.SaveChangesAsync();

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Variantes)
            .SingleAsync(item => item.Id == product.Id);
        var variant = Assert.Single(persisted.Variantes);

        Assert.Equal(123.456m, variant.PesoLiquidoGramas);
        Assert.Equal(78.9m, variant.VolumeLiquidoMililitros);
    }

    [SqlServerIntegrationFact]
    public async Task CharacteristicsAndMeasurements_PersistWithProductScopedReferentialIntegrity()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Velas", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var firstVariant = ProdutoVariante.Criar("100 g Lavanda", "UN", 35m, padrao: true);
        var secondVariant = ProdutoVariante.Criar("200 g Baunilha", "KIT", 55m);
        var product = Produto.Criar("Vela Aromática", null, category, [firstVariant, secondVariant]);
        var weight = product.AdicionarCaracteristica("Peso", TipoMedidaFisica.PesoLiquido);
        var fragrance = product.AdicionarCaracteristica("Fragrância");
        var weight100 = weight.AdicionarValorMedida(100m, "g");
        var weight200 = weight.AdicionarValorMedida(200m, "g");
        var lavender = fragrance.AdicionarValor("Lavanda");
        var vanilla = fragrance.AdicionarValor("Baunilha");
        product.AssociarValorCaracteristica(firstVariant.Id, weight.Id, weight100.Id);
        product.AssociarValorCaracteristica(firstVariant.Id, fragrance.Id, lavender.Id);
        product.AssociarValorCaracteristica(secondVariant.Id, fragrance.Id, vanilla.Id);
        product.AssociarValorCaracteristica(secondVariant.Id, weight.Id, weight200.Id);

        var otherProduct = Produto.CriarSimples("Outro produto", null, category, "UN", 10m);
        context.Produtos.AddRange(product, otherProduct);
        await context.SaveChangesAsync();

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Caracteristicas)
                .ThenInclude(characteristic => characteristic.Valores)
            .Include(item => item.Variantes)
                .ThenInclude(variant => variant.CaracteristicaValores)
                    .ThenInclude(association => association.Caracteristica)
            .Include(item => item.Variantes)
                .ThenInclude(variant => variant.CaracteristicaValores)
                    .ThenInclude(association => association.Valor)
            .SingleAsync(item => item.Id == product.Id);
        var persistedVariants = persisted.Variantes.ToDictionary(item => item.Id);
        Assert.Equal(2, persisted.Caracteristicas.Count);
        Assert.Equal(2, persisted.Caracteristicas.Single(item => item.Id == weight.Id).Valores.Count);
        Assert.Equal(100m, persistedVariants[firstVariant.Id].PesoLiquidoGramas);
        Assert.Equal(200m, persistedVariants[secondVariant.Id].PesoLiquidoGramas);
        Assert.Equal(2, persistedVariants[firstVariant.Id].CaracteristicaValores.Count);
        Assert.Equal(2, persistedVariants[secondVariant.Id].CaracteristicaValores.Count);
        await using var constraintContext = database.CreateVerificationContext();
        Assert.Equal(32, await constraintContext.Database
            .SqlQueryRaw<int>(
                "SELECT DATALENGTH([CombinacaoHash]) AS [Value] FROM [ProdutoVariantes] WHERE [Id] = {0}",
                firstVariant.Id)
            .SingleAsync());

        var duplicateCharacteristic = constraintContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoCaracteristicas]
                ([Id], [ProdutoId], [Nome], [NomeNormalizado], [TipoMedidaFisica])
            VALUES
                ({Guid.NewGuid()}, {product.Id}, {" peso "}, {"PESO"}, {null});
            """);
        await Assert.ThrowsAsync<SqlException>(() => duplicateCharacteristic);

        var duplicateValue = constraintContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoCaracteristicaValores]
                ([Id], [ProdutoId], [ProdutoCaracteristicaId], [Nome], [NomeNormalizado], [ValorMedidaCanonico])
            VALUES
                ({Guid.NewGuid()}, {product.Id}, {weight.Id}, {"100 G"}, {"100 G"}, {100m});
            """);
        await Assert.ThrowsAsync<SqlException>(() => duplicateValue);

        var duplicateCharacteristicValue = constraintContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoVarianteCaracteristicaValores]
                ([ProdutoId], [ProdutoVarianteId], [ProdutoCaracteristicaId], [ProdutoCaracteristicaValorId])
            VALUES
                ({product.Id}, {firstVariant.Id}, {weight.Id}, {weight200.Id});
            """);
        await Assert.ThrowsAsync<SqlException>(() => duplicateCharacteristicValue);

        var crossProductValue = constraintContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProdutoVarianteCaracteristicaValores]
                ([ProdutoId], [ProdutoVarianteId], [ProdutoCaracteristicaId], [ProdutoCaracteristicaValorId])
            VALUES
                ({otherProduct.Id},
                 {Assert.Single(otherProduct.Variantes).Id},
                 {weight.Id},
                 {weight100.Id});
            """);
        await Assert.ThrowsAsync<SqlException>(() => crossProductValue);

        var duplicateCombination = constraintContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [ProdutoVariantes]
            SET [CombinacaoHash] = (
                SELECT [CombinacaoHash] FROM [ProdutoVariantes] WHERE [Id] = {firstVariant.Id})
            WHERE [Id] = {secondVariant.Id};
            """);
        await Assert.ThrowsAsync<SqlException>(() => duplicateCombination);
    }

    [SqlServerIntegrationFact]
    public async Task CombinationHashIndex_RejectsConcurrentDuplicates()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Velas", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();
        var product = Produto.CriarSimples("Vela concorrente", null, category, "UN", 25m);
        context.Produtos.Add(product);
        await context.SaveChangesAsync();

        var duplicateHash = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstInsert = InsertConcurrentCombinationAsync(
            database,
            start.Task,
            product.Id,
            Guid.NewGuid(),
            "Combinação concorrente A",
            "COMBINAÇÃO CONCORRENTE A",
            duplicateHash);
        var secondInsert = InsertConcurrentCombinationAsync(
            database,
            start.Task,
            product.Id,
            Guid.NewGuid(),
            "Combinação concorrente B",
            "COMBINAÇÃO CONCORRENTE B",
            duplicateHash);
        start.SetResult(true);

        var outcomes = await Task.WhenAll(firstInsert, secondInsert);

        Assert.Single(outcomes, outcome => outcome is null);
        var conflict = Assert.IsType<SqlException>(Assert.Single(outcomes, outcome => outcome is not null));
        Assert.True(conflict.Number is 2601 or 2627);
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

        var legacyVariantId = Guid.NewGuid();
        await InsertVariantAsync(
            constraintContext,
            productId,
            legacyVariantId,
            "Unidade legada",
            "UNIDADE LEGADA",
            "G",
            1m,
            active: true,
            isDefault: false);
        var persistedLegacyUnit = await constraintContext.ProdutoVariantes
            .SingleAsync(variant => variant.Id == legacyVariantId);
        Assert.Equal("G", persistedLegacyUnit.UnidadeVenda);

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
                new ProdutoVarianteInput("100 g", "UN", 20m),
                [new ProdutoVarianteInput("200 g", "UN", 35m)]));
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
            new ProdutoVarianteInput("250 g", "UN", 39.90m));
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
    public async Task AdminProductCases_PersistAndReloadCharacteristicsAndRollbackInvalidEdits()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using (var context = database.CreateVerificationContext())
        {
            var category = Categoria.Criar("Velas", null, 0);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var manager = database.Services.GetRequiredService<IGerenciarProdutos>();
            var created = await manager.CadastrarAsync(new ProdutoCadastroInput(
                "Vela de lavanda",
                null,
                category.Id,
                new ProdutoVarianteInput(
                    "100 g branca",
                    "UN",
                    35m,
                    Caracteristicas:
                    [
                        new(null, "peso", null, "100g"),
                        new(null, "cor", null, "branca")
                    ]),
                [
                    new ProdutoVarianteInput(
                        "200 g preta",
                        "KIT",
                        55m,
                        Caracteristicas:
                        [
                            new(null, "peso", null, "200g"),
                            new(null, "cor", null, "preta")
                        ])
                ],
                Simples: false,
                Caracteristicas:
                [
                    new(
                        "peso",
                        null,
                        "Peso",
                        nameof(TipoMedidaFisica.PesoLiquido),
                        [
                            new("100g", null, null, 100m),
                            new("200g", null, null, 200m)
                        ]),
                    new(
                        "cor",
                        null,
                        "Cor",
                        null,
                        [
                            new("branca", null, "Branca", null),
                            new("preta", null, "Preta", null)
                        ])
                ]));

            Assert.Equal(ProdutoStatus.Success, created.Status);
            var productId = Assert.IsType<Produto>(created.Produto).Id;
            var persisted = await context.Produtos
                .Include(item => item.Caracteristicas)
                    .ThenInclude(characteristic => characteristic.Valores)
                .Include(item => item.Variantes)
                    .ThenInclude(variant => variant.CaracteristicaValores)
                        .ThenInclude(association => association.Caracteristica)
                .Include(item => item.Variantes)
                    .ThenInclude(variant => variant.CaracteristicaValores)
                        .ThenInclude(association => association.Valor)
                .SingleAsync(item => item.Id == productId);
            var originalCharacteristicIds = persisted.Caracteristicas.Select(item => item.Id).ToArray();
            var originalVariantIds = persisted.Variantes.Select(item => item.Id).ToArray();
            var originalValueIds = persisted.Caracteristicas
                .SelectMany(item => item.Valores)
                .Select(item => item.Id)
                .ToArray();
            Assert.Equal(2, persisted.Caracteristicas.Count);
            Assert.Equal(2, persisted.Variantes.Count);
            Assert.Equal(100m, persisted.Variantes.Single(item => item.Padrao).PesoLiquidoGramas);
            Assert.Equal(200m, persisted.Variantes.Single(item => !item.Padrao).PesoLiquidoGramas);

            var persistedWeight = persisted.Caracteristicas.Single(item =>
                item.TipoMedidaFisica == TipoMedidaFisica.PesoLiquido);
            var weight100 = persistedWeight.Valores.Single(item => item.ValorMedidaCanonico == 100m);
            var weight200 = persistedWeight.Valores.Single(item => item.ValorMedidaCanonico == 200m);
            var persistedColor = persisted.Caracteristicas.Single(item => item.Nome == "Cor");
            var white = persistedColor.Valores.Single(item => item.Nome == "Branca");
            var black = persistedColor.Valores.Single(item => item.Nome == "Preta");
            var defaultVariant = persisted.Variantes.Single(item => item.Padrao);
            var otherVariant = persisted.Variantes.Single(item => !item.Padrao);
            var validEdit = await manager.EditarAsync(
                productId,
                new ProdutoEdicaoInput(
                    "Vela de lavanda atualizada",
                    null,
                    category.Id,
                    [
                        new ProdutoCaracteristicaInput(
                            "peso",
                            persistedWeight.Id,
                            persistedWeight.Nome,
                            nameof(TipoMedidaFisica.PesoLiquido),
                            [
                                new("peso-100", weight100.Id, null, 100m),
                                new("peso-200", weight200.Id, null, 200m)
                            ]),
                        new ProdutoCaracteristicaInput(
                            "cor",
                            persistedColor.Id,
                            persistedColor.Nome,
                            null,
                            [
                                new("branca", white.Id, white.Nome, null),
                                new("preta", black.Id, black.Nome, null),
                                new("amarela", null, "Amarela", null)
                            ])
                    ],
                    [
                        new ProdutoVarianteInput(
                            defaultVariant.Identificacao,
                            defaultVariant.UnidadeVenda,
                            defaultVariant.PrecoVenda,
                            defaultVariant.Id,
                            Caracteristicas:
                            [
                                new(persistedWeight.Id, null, weight100.Id, null),
                                new(persistedColor.Id, null, white.Id, null)
                            ],
                            Padrao: true),
                        new ProdutoVarianteInput(
                            otherVariant.Identificacao,
                            otherVariant.UnidadeVenda,
                            otherVariant.PrecoVenda,
                            otherVariant.Id,
                            Caracteristicas:
                            [
                                new(persistedWeight.Id, null, weight200.Id, null),
                                new(persistedColor.Id, null, black.Id, null)
                            ]),
                        new ProdutoVarianteInput(
                            "100 g amarela",
                            "UN",
                            39m,
                            Caracteristicas:
                            [
                                new(null, "peso", null, "peso-100"),
                                new(null, "cor", null, "amarela")
                            ])
                    ]));
            Assert.Equal(ProdutoStatus.Success, validEdit.Status);
            Assert.Equal(
                originalCharacteristicIds.Order(),
                validEdit.Produto!.Caracteristicas.Select(item => item.Id).Order());
            Assert.Equal(
                originalVariantIds.Order(),
                originalVariantIds.Intersect(validEdit.Produto.Variantes.Select(item => item.Id)).Order());
            Assert.Equal(3, validEdit.Produto.Variantes.Count);
            var variantIdsAfterEdit = validEdit.Produto.Variantes.Select(item => item.Id).ToArray();
            Assert.Contains(
                validEdit.Produto.Caracteristicas.Single(item => item.Id == persistedColor.Id).Valores,
                item => item.Id == white.Id);

            var invalidEdit = await manager.EditarAsync(
                productId,
                new ProdutoEdicaoInput(
                    "Não deve persistir",
                    null,
                    category.Id,
                    [new ProdutoCaracteristicaInput(
                        null,
                        Guid.NewGuid(),
                        "Característica inexistente",
                        null)]));
            Assert.Equal(ProdutoStatus.ValidationFailed, invalidEdit.Status);

            await using var rollbackVerification = database.CreateVerificationContext();
            var afterInvalidEdit = await rollbackVerification.Produtos
                .Include(item => item.Caracteristicas)
                    .ThenInclude(characteristic => characteristic.Valores)
                .Include(item => item.Variantes)
                .SingleAsync(item => item.Id == productId);
            Assert.Equal("Vela de lavanda atualizada", afterInvalidEdit.Nome);
            Assert.Equal(originalCharacteristicIds, afterInvalidEdit.Caracteristicas.Select(item => item.Id).ToArray());
            Assert.Equal(variantIdsAfterEdit.Order(), afterInvalidEdit.Variantes.Select(item => item.Id).Order());
            Assert.All(
                originalValueIds,
                id => Assert.Contains(
                    afterInvalidEdit.Caracteristicas.SelectMany(item => item.Valores),
                    value => value.Id == id));
        }
    }

    [SqlServerIntegrationFact]
    public async Task ExistingLegacyUnitG_IsPreservedDuringUnrelatedProductEdit()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: AddProdutosEVariantesMigration);
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Sabonetes", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Produtos] ([Id], [Nome], [Descricao], [CategoriaId], [Publicado])
            VALUES ({productId}, {"Produto legado"}, {null}, {category.Id}, {false});

            INSERT INTO [ProdutoVariantes]
                ([Id], [ProdutoId], [Identificacao], [IdentificacaoNormalizada], [UnidadeVenda],
                 [PrecoVenda], [Ativa], [Padrao])
            VALUES
                ({variantId}, {productId}, {"Padrão legado"}, {"PADRÃO LEGADO"}, {"G"},
                 {19m}, {true}, {true});
            """);
        await context.Database.MigrateAsync();

        var manager = database.Services.GetRequiredService<IGerenciarProdutos>();
        var result = await manager.EditarAsync(
            productId,
            new ProdutoEdicaoInput("Produto legado atualizado", null, category.Id));
        Assert.Equal(ProdutoStatus.Success, result.Status);

        await using var verificationContext = database.CreateVerificationContext();
        var persisted = await verificationContext.Produtos
            .Include(item => item.Variantes)
            .SingleAsync(item => item.Id == productId);
        var variant = Assert.Single(persisted.Variantes);
        Assert.Equal("Produto legado atualizado", persisted.Nome);
        Assert.Equal(variantId, variant.Id);
        Assert.Equal("G", variant.UnidadeVenda);
        Assert.Equal(19m, variant.PrecoVenda);
        Assert.True(variant.Ativa);
        Assert.True(variant.Padrao);
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
                [new ProdutoVarianteInput("200 g", "KIT", 38m)]));
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

    private static ProdutoImagem AdicionarImagem(
        Produto produto,
        string chave,
        string nomeOriginal) =>
        produto.AdicionarImagem(
            chave,
            nomeOriginal,
            "image/jpeg",
            1024,
            120,
            120);

    private static async Task<Exception?> InsertConcurrentImageSlotAsync(
        SqlServerTestDatabase database,
        Task start,
        Guid productId,
        string storageKey)
    {
        await start.ConfigureAwait(false);
        await using var context = database.CreateVerificationContext();
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [ProdutoImagens]
                    ([Id], [ProdutoId], [ChaveArmazenamento], [NomeOriginal], [TipoConteudo],
                     [TamanhoBytes], [LarguraPixels], [AlturaPixels], [OrdemExibicao], [SlotGaleria],
                     [Principal], [CriadoEmUtc])
                VALUES
                    ({Guid.NewGuid()}, {productId}, {storageKey}, {"concorrente.jpg"}, {"image/jpeg"},
                     {1024L}, {100}, {100}, {5}, {5}, {false}, {DateTimeOffset.UtcNow});
                """);
            return null;
        }
        catch (SqlException exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> InsertConcurrentCombinationAsync(
        SqlServerTestDatabase database,
        Task start,
        Guid productId,
        Guid variantId,
        string identification,
        string normalizedIdentification,
        byte[] combinationHash)
    {
        await start.ConfigureAwait(false);
        await using var context = database.CreateVerificationContext();
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [ProdutoVariantes]
                    ([Id], [ProdutoId], [Identificacao], [IdentificacaoNormalizada], [UnidadeVenda],
                     [PrecoVenda], [Ativa], [Padrao], [CombinacaoHash])
                VALUES
                    ({variantId}, {productId}, {identification}, {normalizedIdentification}, {"UN"},
                     {30m}, {false}, {false}, {combinationHash});
                """);
            return null;
        }
        catch (SqlException exception)
        {
            return exception;
        }
    }

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
