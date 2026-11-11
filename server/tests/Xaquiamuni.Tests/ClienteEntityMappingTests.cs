using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public class ClienteEntityMappingTests
{
    [Fact]
    public void ClientePerfil_MapsOneToOneToIdentityUserWithoutRequiringProfilesForAllUsers()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.Model;
        var profile = model.FindEntityType(typeof(ClientePerfil))!;
        var user = model.FindEntityType(typeof(XaquiamuniUser))!;
        var primaryKey = profile.FindPrimaryKey();
        Assert.NotNull(primaryKey);
        var foreignKey = Assert.Single(profile.GetForeignKeys());

        Assert.Equal("ClientePerfis", profile.GetTableName());
        Assert.Equal(nameof(ClientePerfil.UserId), Assert.Single(primaryKey.Properties).Name);
        Assert.Equal(user, foreignKey.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsUnique);
        Assert.Equal(nameof(ClientePerfil.UserId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(nameof(XaquiamuniUser.Id), Assert.Single(foreignKey.PrincipalKey.Properties).Name);
        Assert.Equal("AspNetUsers", user.GetTableName());

        var identityEmailIndex = Assert.Single(user.GetIndexes(), index =>
            index.Properties.Any(property => property.Name == nameof(XaquiamuniUser.NormalizedEmail)));
        Assert.True(identityEmailIndex.IsUnique);
        Assert.Equal("[NormalizedEmail] IS NOT NULL", identityEmailIndex.GetFilter());
    }

    [Fact]
    public void ClientePerfil_MapsRequiredNameAndProtectedCpfToSeparateColumns()
    {
        using var dbContext = CreateDbContext();
        var profile = dbContext.Model.FindEntityType(typeof(ClientePerfil))!;
        var table = StoreObjectIdentifier.Table("ClientePerfis", null);
        var name = profile.FindProperty(nameof(ClientePerfil.NomeCompleto))!;
        var cpfNavigation = profile.FindNavigation(nameof(ClientePerfil.CpfProtegido))!;
        var cpf = cpfNavigation.TargetEntityType;

        Assert.Equal(150, name.GetMaxLength());
        Assert.False(name.IsNullable);
        Assert.Equal("NomeCompleto", name.GetColumnName(table));
        Assert.True(cpfNavigation.ForeignKey.IsOwnership);
        Assert.True(cpfNavigation.ForeignKey.IsRequired);

        AssertPropertyMapping(cpf, nameof(CpfProtegido.Ciphertext), "CpfCiphertext", 16, false, "nvarchar(16)", table);
        AssertPropertyMapping(cpf, nameof(CpfProtegido.Nonce), "CpfNonce", 16, false, "nvarchar(16)", table);
        AssertPropertyMapping(cpf, nameof(CpfProtegido.AuthenticationTag), "CpfAuthenticationTag", 24, false, "nvarchar(24)", table);
        AssertPropertyMapping(cpf, nameof(CpfProtegido.Fingerprint), "CpfFingerprint", 64, false, "nvarchar(64)", table);
        AssertPropertyMapping(cpf, nameof(CpfProtegido.EncryptionKeyVersion), "CpfEncryptionKeyVersion", 32, false, "nvarchar(32)", table);
        AssertPropertyMapping(cpf, nameof(CpfProtegido.FingerprintKeyVersion), "CpfFingerprintKeyVersion", 32, false, "nvarchar(32)", table);

        var fingerprintIndex = Assert.Single(cpf.GetIndexes(), index =>
            index.Properties.Any(property => property.Name == nameof(CpfProtegido.Fingerprint)));
        Assert.True(fingerprintIndex.IsUnique);
        Assert.Equal("UX_ClientePerfis_CpfFingerprint", fingerprintIndex.GetDatabaseName());
    }

    [Fact]
    public void ClienteEndereco_MapsOneToManyWithRestrictiveDelete()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.Model;
        var address = model.FindEntityType(typeof(ClienteEndereco))!;
        var profile = model.FindEntityType(typeof(ClientePerfil))!;
        var foreignKey = Assert.Single(address.GetForeignKeys());

        Assert.Equal("ClienteEnderecos", address.GetTableName());
        Assert.Equal(nameof(ClienteEndereco.Id), Assert.Single(address.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(profile, foreignKey.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.False(foreignKey.IsUnique);
        Assert.Equal(nameof(ClientePerfil.UserId), Assert.Single(foreignKey.PrincipalKey.Properties).Name);
        Assert.Equal(nameof(ClienteEndereco.ClienteUserId), Assert.Single(foreignKey.Properties).Name);
    }

    [Fact]
    public void ClienteEndereco_MapsRequiredAndOptionalFieldsWithExpectedTypesAndLengths()
    {
        using var dbContext = CreateDbContext();
        var address = dbContext.Model.FindEntityType(typeof(ClienteEndereco))!;
        var table = StoreObjectIdentifier.Table("ClienteEnderecos", null);

        AssertPropertyMapping(address, nameof(ClienteEndereco.Cep), "Cep", 8, false, "nvarchar(8)", table);
        AssertPropertyMapping(address, nameof(ClienteEndereco.Uf), "Uf", 2, false, "nvarchar(2)", table);
        AssertPropertyMapping(address, nameof(ClienteEndereco.Cidade), "Cidade", 100, false, "nvarchar(100)", table);
        AssertPropertyMapping(address, nameof(ClienteEndereco.Bairro), "Bairro", 100, false, "nvarchar(100)", table);
        AssertPropertyMapping(address, nameof(ClienteEndereco.Logradouro), "Logradouro", 150, false, "nvarchar(150)", table);
        AssertPropertyMapping(address, nameof(ClienteEndereco.Numero), "Numero", 20, false, "nvarchar(20)", table);
        AssertPropertyMapping(address, nameof(ClienteEndereco.Complemento), "Complemento", 100, true, "nvarchar(100)", table);
        AssertPropertyMapping(address, nameof(ClienteEndereco.IsDefault), "IsDefault", null, false, "bit", table);
    }

    [Fact]
    public void ClienteEndereco_HasUniqueFilteredDefaultIndexPerCustomer()
    {
        using var dbContext = CreateDbContext();
        var address = dbContext.Model.FindEntityType(typeof(ClienteEndereco))!;
        var foreignKeyIndex = Assert.Single(address.GetIndexes(), index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(ClienteEndereco.ClienteUserId)
            && !index.IsUnique);
        var defaultIndex = Assert.Single(address.GetIndexes(), index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(ClienteEndereco.ClienteUserId)
            && index.GetFilter() is not null);

        Assert.Equal("IX_ClienteEnderecos_ClienteUserId", foreignKeyIndex.GetDatabaseName());
        Assert.True(defaultIndex.IsUnique);
        Assert.Equal("UX_ClienteEnderecos_DefaultPerClient", defaultIndex.GetDatabaseName());
        Assert.Equal("[IsDefault] = 1", defaultIndex.GetFilter());
    }

    private static XaquiamuniDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniModelOnly")
            .Options;
        return new XaquiamuniDbContext(options);
    }

    private static void AssertPropertyMapping(
        IEntityType entityType,
        string propertyName,
        string columnName,
        int? maxLength,
        bool nullable,
        string storeType,
        StoreObjectIdentifier table)
    {
        var property = entityType.FindProperty(propertyName)!;

        Assert.Equal(maxLength, property.GetMaxLength());
        Assert.Equal(nullable, property.IsNullable);
        Assert.Equal(columnName, property.GetColumnName(table));
        Assert.Equal(storeType, property.GetRelationalTypeMapping().StoreType);
    }
}
