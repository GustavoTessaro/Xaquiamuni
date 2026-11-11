using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class CadastrarClienteSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task CadastroBemSucedido_PersisteIdentityPerfilCpfProtegidoERoleEnderecoPadrao()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var useCase = database.Services.GetRequiredService<ICadastrarCliente>();
        var input = ValidInput();

        var result = await useCase.ExecuteAsync(input);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.UserId);
        await using var verification = database.CreateVerificationContext();
        var user = await verification.Users.SingleAsync(user => user.Id == result.UserId);
        var profile = await verification.Set<ClientePerfil>().SingleAsync(profile => profile.UserId == result.UserId);
        var address = await verification.Set<ClienteEndereco>().SingleAsync(address =>
            address.ClienteUserId == result.UserId);
        var role = await verification.Roles.SingleAsync(role => role.Name == "CLIENTE");
        var userRole = await verification.UserRoles.SingleAsync(userRole =>
            userRole.UserId == result.UserId && userRole.RoleId == role.Id);
        var cpf = CreateCpf(input.Cpf!);
        var protectionService = database.Services.GetRequiredService<ICpfProtectionService>();
        var userManager = database.Services.GetRequiredService<UserManager<XaquiamuniUser>>();
        var identityUser = await userManager.FindByIdAsync(result.UserId!.Value.ToString());

        Assert.Equal(input.Email, user.Email);
        Assert.Equal(input.Telefone, user.PhoneNumber);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(input.Senha, user.PasswordHash);
        Assert.NotNull(identityUser);
        Assert.True(await userManager.CheckPasswordAsync(identityUser!, input.Senha!));
        Assert.Equal(result.UserId, profile.UserId);
        Assert.Equal(64, profile.CpfProtegido.Fingerprint.Length);
        Assert.Equal(
            protectionService.CreateFingerprintForLookup(cpf),
            profile.CpfProtegido.Fingerprint);
        Assert.Equal(cpf, protectionService.RecoverForAuthorizedUse(profile.CpfProtegido));
        Assert.NotEqual(input.Cpf, profile.CpfProtegido.Ciphertext);
        Assert.Equal("95000000", address.Cep);
        Assert.True(address.IsDefault);
        Assert.Equal(result.UserId, userRole.UserId);
    }

    [SqlServerIntegrationFact]
    public async Task FalhaNaPersistenciaDoPerfil_DesfazUsuarioEAssociacaoIdentityNaTransacaoReal()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(failProfilePersistence: true);
        var useCase = database.Services.GetRequiredService<ICadastrarCliente>();

        var result = await useCase.ExecuteAsync(ValidInput());

        Assert.Equal(CadastroClienteStatus.PersistenceFailed, result.Status);
        await using var verification = database.CreateVerificationContext();
        Assert.Equal(0, await verification.Users.CountAsync());
        Assert.Equal(0, await verification.UserRoles.CountAsync());
        Assert.Equal(0, await verification.Set<ClientePerfil>().CountAsync());
        Assert.Equal(0, await verification.Set<ClienteEndereco>().CountAsync());
        Assert.Equal(1, await verification.Roles.CountAsync(role => role.Name == "CLIENTE"));
    }

    [SqlServerIntegrationFact]
    public async Task EmailDuplicado_NaoCriaRegistrosAdicionais()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var useCase = database.Services.GetRequiredService<ICadastrarCliente>();
        var firstInput = ValidInput();

        var first = await useCase.ExecuteAsync(firstInput);
        var duplicate = await useCase.ExecuteAsync(
            With(firstInput, email: firstInput.Email!, cpf: "111.444.777-35"));

        Assert.True(first.Succeeded);
        Assert.Equal(CadastroClienteStatus.EmailAlreadyRegistered, duplicate.Status);
        await using var verification = database.CreateVerificationContext();
        Assert.Equal(1, await verification.Users.CountAsync());
        Assert.Equal(1, await verification.UserRoles.CountAsync());
        Assert.Equal(1, await verification.Set<ClientePerfil>().CountAsync());
        Assert.Equal(1, await verification.Set<ClienteEndereco>().CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task CpfDuplicadoSobIndiceUnico_NaoDeixaIdentityParcial()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var useCase = database.Services.GetRequiredService<ICadastrarCliente>();

        var first = await useCase.ExecuteAsync(ValidInput());
        var duplicate = await useCase.ExecuteAsync(With(ValidInput(), email: "outra@example.com"));

        Assert.True(first.Succeeded);
        Assert.Equal(CadastroClienteStatus.CpfAlreadyRegistered, duplicate.Status);
        await using var verification = database.CreateVerificationContext();
        Assert.Equal(1, await verification.Users.CountAsync());
        Assert.Equal(1, await verification.UserRoles.CountAsync());
        Assert.Equal(1, await verification.Set<ClientePerfil>().CountAsync());
        Assert.Equal(1, await verification.Set<ClienteEndereco>().CountAsync());
    }

    private static CadastroClienteInput ValidInput() => new()
    {
        NomeCompleto = "Cliente Integração",
        Cpf = "529.982.247-25",
        Email = $"cliente-{Guid.NewGuid():N}@example.com",
        Telefone = "+55 (11) 91234-5678",
        Senha = "Senha de teste 987!",
        EnderecoInicial = new EnderecoInicialInput
        {
            Cep = "95000-000",
            Uf = "RS",
            Cidade = "Vacaria",
            Bairro = "Centro",
            Logradouro = "Rua das Flores",
            Numero = "10",
            Complemento = "Casa 2"
        }
    };

    private static CadastroClienteInput With(
        CadastroClienteInput source,
        string email,
        string? cpf = null) =>
        new()
        {
            NomeCompleto = source.NomeCompleto,
            Cpf = cpf ?? source.Cpf,
            Email = email,
            Telefone = source.Telefone,
            Senha = source.Senha,
            EnderecoInicial = source.EnderecoInicial
        };

    private static Cpf CreateCpf(string value)
    {
        Assert.True(Cpf.TryCreate(value, out var cpf));
        return Assert.IsType<Cpf>(cpf);
    }
}
