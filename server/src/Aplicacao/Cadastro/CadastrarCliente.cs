using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Aplicacao.Cadastro;

public sealed class CadastrarCliente : ICadastrarCliente
{
    private const string RoleCliente = "CLIENTE";

    private readonly CadastroClienteInputValidator _validator;
    private readonly ICpfProtectionService _cpfProtectionService;
    private readonly IClienteIdentityService _identityService;
    private readonly IClienteRepository _clienteRepository;
    private readonly IClienteTransactionFactory _transactionFactory;

    public CadastrarCliente(
        CadastroClienteInputValidator validator,
        ICpfProtectionService cpfProtectionService,
        IClienteIdentityService identityService,
        IClienteRepository clienteRepository,
        IClienteTransactionFactory transactionFactory)
    {
        _validator = validator;
        _cpfProtectionService = cpfProtectionService;
        _identityService = identityService;
        _clienteRepository = clienteRepository;
        _transactionFactory = transactionFactory;
    }

    public async Task<CadastroClienteResult> ExecuteAsync(
        CadastroClienteInput? input,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.Valido)
        {
            return CadastroClienteResult.Invalid(validation.Erros);
        }

        if (!Cpf.TryCreate(input!.Cpf, out var cpf) || cpf is null)
        {
            return CadastroClienteResult.Invalid(
            [
                new CadastroClienteInputValidationError("Cpf", "O CPF informado é inválido.")
            ]);
        }

        var fingerprint = _cpfProtectionService.CreateFingerprintForLookup(cpf);
        var protectedCpf = _cpfProtectionService.Protect(cpf);
        if (!string.Equals(fingerprint, protectedCpf.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A proteção do CPF não produziu uma impressão digital consistente.");
        }

        if (!await _identityService.RoleExistsAsync(RoleCliente, cancellationToken).ConfigureAwait(false))
        {
            return CadastroClienteResult.Failure(CadastroClienteStatus.RoleUnavailable);
        }

        var userId = Guid.NewGuid();
        await using var transaction = await _transactionFactory
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var identityResult = await _identityService.CreateUserAsync(
            userId,
            input.Email!.Trim(),
            input.Telefone!.Trim(),
            input.Senha!,
            cancellationToken).ConfigureAwait(false);

        if (!identityResult.Succeeded)
        {
            return CadastroClienteResult.Failure(identityResult.Status switch
            {
                ClienteIdentityOperationStatus.DuplicateEmail => CadastroClienteStatus.EmailAlreadyRegistered,
                ClienteIdentityOperationStatus.PersistenceFailed => CadastroClienteStatus.PersistenceFailed,
                _ => CadastroClienteStatus.IdentityCreationFailed
            });
        }

        var roleResult = await _identityService.AddToRoleAsync(
            userId,
            RoleCliente,
            cancellationToken).ConfigureAwait(false);
        if (!roleResult.Succeeded)
        {
            return CadastroClienteResult.Failure(
                roleResult.Status == ClienteIdentityOperationStatus.PersistenceFailed
                    ? CadastroClienteStatus.PersistenceFailed
                    : CadastroClienteStatus.RoleAssignmentFailed);
        }

        var profile = ClientePerfil.Criar(userId, input.NomeCompleto, protectedCpf);
        var addressInput = input.EnderecoInicial!;
        var address = ClienteEndereco.Criar(
            Guid.NewGuid(),
            userId,
            addressInput.Cep,
            addressInput.Uf,
            addressInput.Cidade,
            addressInput.Bairro,
            addressInput.Logradouro,
            addressInput.Numero,
            addressInput.Complemento,
            isDefault: true);

        _clienteRepository.Adicionar(profile);
        _clienteRepository.Adicionar(address);

        try
        {
            await transaction.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ClienteCpfDuplicadoException)
        {
            return CadastroClienteResult.Failure(CadastroClienteStatus.CpfAlreadyRegistered);
        }
        catch (ClientePersistenceException)
        {
            return CadastroClienteResult.Failure(CadastroClienteStatus.PersistenceFailed);
        }

        return CadastroClienteResult.Success(userId);
    }
}
