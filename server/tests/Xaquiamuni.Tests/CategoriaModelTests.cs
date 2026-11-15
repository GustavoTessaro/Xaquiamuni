using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public sealed class CategoriaModelTests
{
    [Fact]
    public void Model_MapsUniqueNormalizedNameAndNonNegativeOrderConstraint()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Categoria))!;
        var nameProperty = entity.FindProperty(nameof(Categoria.NomeNormalizado))!;
        var nameIndex = Assert.Single(entity.GetIndexes(), index =>
            index.Properties.SequenceEqual([nameProperty]));
        var constraints = entity
            .GetCheckConstraints()
            .ToDictionary(constraint => constraint.Name!, constraint => constraint.Sql!);

        Assert.Equal("Categorias", entity.GetTableName());
        Assert.Equal("Latin1_General_100_BIN2", nameProperty.GetCollation());
        Assert.True(nameIndex.IsUnique);
        Assert.Equal("UX_Categorias_NomeNormalizado", nameIndex.GetDatabaseName());
        Assert.Equal("[OrdemExibicao] >= 0", constraints["CK_Categorias_OrdemExibicao_NonNegative"]);
    }

    [Fact]
    public void ModelSnapshot_HasNoPendingChanges()
    {
        using var context = CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static XaquiamuniDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniCategoriaModelTest")
            .Options;
        return new XaquiamuniDbContext(options);
    }
}
