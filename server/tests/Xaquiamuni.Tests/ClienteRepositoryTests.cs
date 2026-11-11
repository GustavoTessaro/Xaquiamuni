using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public class ClienteRepositoryTests
{
    [Fact]
    public void Contrato_UsaSomenteTiposDoDominioENaoExpoeOperacaoDeConfirmacao()
    {
        var methods = typeof(IClienteRepository).GetMethods();

        Assert.Equal(2, methods.Length);
        Assert.All(methods, method =>
        {
            Assert.Equal(typeof(void), method.ReturnType);
            Assert.Single(method.GetParameters());
        });
        Assert.Contains(methods, method => method.GetParameters()[0].ParameterType == typeof(ClientePerfil));
        Assert.Contains(methods, method => method.GetParameters()[0].ParameterType == typeof(ClienteEndereco));
        Assert.DoesNotContain(methods, method => method.Name.Contains("Save", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Adicionar_RastreiaPerfilEEnderecoSemPersistir()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniRepositoryTest")
            .Options;
        using var dbContext = new XaquiamuniDbContext(options);
        var repository = new ClienteRepository(dbContext);
        var userId = Guid.NewGuid();
        var profile = ClientePerfil.Criar(userId, "Ana Silva", CreateProtectedCpf());
        var address = ClienteEndereco.Criar(
            Guid.NewGuid(),
            userId,
            "95000-000",
            "RS",
            "Vacaria",
            "Centro",
            "Rua das Flores",
            "10",
            null,
            isDefault: true);

        repository.Adicionar(profile);
        repository.Adicionar(address);

        Assert.Equal(EntityState.Added, dbContext.Entry(profile).State);
        Assert.Equal(EntityState.Added, dbContext.Entry(address).State);
        Assert.True(dbContext.ChangeTracker.HasChanges());
    }

    [Fact]
    public void Adicionar_RejeitaEntidadesNulas()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniRepositoryTest")
            .Options;
        using var dbContext = new XaquiamuniDbContext(options);
        var repository = new ClienteRepository(dbContext);

        Assert.Throws<ArgumentNullException>(() => repository.Adicionar((ClientePerfil)null!));
        Assert.Throws<ArgumentNullException>(() => repository.Adicionar((ClienteEndereco)null!));
    }

    private static CpfProtegido CreateProtectedCpf() =>
        new(
            Convert.ToBase64String(new byte[11]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[16]),
            new string('A', 64),
            "enc-v1",
            "fp-v1");
}
