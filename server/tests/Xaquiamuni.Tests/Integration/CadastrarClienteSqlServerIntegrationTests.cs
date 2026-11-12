using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class CadastrarClienteSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task CadastroBemSucedido_PersisteIdentityPerfilEnderecoEOutboxNaMesmaTransacao()
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
        var outboxMessage = await verification.EmailOutboxMessages.SingleAsync(message =>
            message.UserId == result.UserId);
        var role = await verification.Roles.SingleAsync(role => role.Name == "CLIENTE");
        var userRole = await verification.UserRoles.SingleAsync(userRole =>
            userRole.UserId == result.UserId && userRole.RoleId == role.Id);
        var cpf = CreateCpf(input.Cpf!);
        var protectionService = database.Services.GetRequiredService<ICpfProtectionService>();
        var userManager = database.Services.GetRequiredService<UserManager<XaquiamuniUser>>();
        var identityUser = await userManager.FindByIdAsync(result.UserId!.Value.ToString());

        AssertSensitiveStringEqual(input.Email, user.Email);
        AssertSensitiveStringEqual(input.Telefone, user.PhoneNumber);
        Assert.NotNull(user.PasswordHash);
        Assert.False(string.Equals(input.Senha, user.PasswordHash, StringComparison.Ordinal));
        Assert.NotNull(identityUser);
        Assert.True(await userManager.CheckPasswordAsync(identityUser!, input.Senha!));
        Assert.Equal(result.UserId, profile.UserId);
        Assert.Equal(EmailOutboxMessageType.EmailConfirmation, outboxMessage.MessageType);
        Assert.Equal(EmailOutboxMessageStatus.Pending, outboxMessage.Status);
        Assert.Equal(0, outboxMessage.Attempts);
        Assert.Equal(result.UserId, outboxMessage.UserId);
        Assert.NotNull(outboxMessage.ActiveDeduplicationKey);
        Assert.NotNull(database.RegistrationTransactionIdAtBegin);
        Assert.Equal(
            database.RegistrationTransactionIdAtBegin,
            database.OutboxTransactionIdAtEnqueue);
        Assert.Equal(64, profile.CpfProtegido.Fingerprint.Length);
        AssertSensitiveStringEqual(
            protectionService.CreateFingerprintForLookup(cpf),
            profile.CpfProtegido.Fingerprint);
        Assert.True(cpf.Equals(protectionService.RecoverForAuthorizedUse(profile.CpfProtegido)));
        Assert.False(string.Equals(input.Cpf, profile.CpfProtegido.Ciphertext, StringComparison.Ordinal));
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
        Assert.Equal(0, await verification.EmailOutboxMessages.CountAsync());
        Assert.Equal(1, await verification.Roles.CountAsync(role => role.Name == "CLIENTE"));
    }

    [SqlServerIntegrationFact]
    public async Task FalhaNoEnfileiramento_DesfazUsuarioPapelPerfilEnderecoEOutbox()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(failOutboxEnqueue: true);
        var useCase = database.Services.GetRequiredService<ICadastrarCliente>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(ValidInput()));

        Assert.NotNull(database.RegistrationTransactionIdAtBegin);
        Assert.Equal(
            database.RegistrationTransactionIdAtBegin,
            database.OutboxTransactionIdAtEnqueue);
        await using var verification = database.CreateVerificationContext();
        Assert.Equal(0, await verification.Users.CountAsync());
        Assert.Equal(0, await verification.UserRoles.CountAsync());
        Assert.Equal(0, await verification.Set<ClientePerfil>().CountAsync());
        Assert.Equal(0, await verification.Set<ClienteEndereco>().CountAsync());
        Assert.Equal(0, await verification.EmailOutboxMessages.CountAsync());
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

    [SqlServerIntegrationFact]
    public async Task IndiceUnicoDaOutbox_RejeitaChaveAtivaDuplicada()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var useCase = database.Services.GetRequiredService<ICadastrarCliente>();
        var registration = await useCase.ExecuteAsync(ValidInput());
        Assert.True(registration.Succeeded);

        await using var verification = database.CreateVerificationContext();
        var existingMessage = await verification.EmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.UserId == registration.UserId);
        var activeDeduplicationKey = Assert.IsType<string>(existingMessage.ActiveDeduplicationKey);
        var now = DateTimeOffset.UtcNow;
        verification.EmailOutboxMessages.Add(new EmailOutboxMessage(
            Guid.NewGuid(),
            registration.UserId!.Value,
            EmailOutboxMessageType.EmailConfirmation,
            EmailOutboxMessageStatus.Pending,
            attempts: 0,
            createdAtUtc: now,
            nextAttemptAtUtc: now,
            activeDeduplicationKey: activeDeduplicationKey));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => verification.SaveChangesAsync());

        Assert.True(HasActiveDeduplicationUniqueViolation(exception));
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

    private static bool HasActiveDeduplicationUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.SqlClient.SqlException sqlException
                && sqlException.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().Any(error =>
                    (error.Number is 2601 or 2627)
                    && error.Message.Contains(
                        "UX_EmailOutbox_ActiveDeduplicationKey",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertSensitiveStringEqual(string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new Xunit.Sdk.XunitException("Sensitive string values do not match.");
        }
    }
}
