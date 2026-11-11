using Xaquiamuni.Aplicacao.Autenticacao;
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
    private readonly IEmailConfirmationOutbox _emailConfirmationOutbox;
    private readonly IClienteTransactionFactory _transactionFactory;

    public CadastrarCliente(
        CadastroClienteInputValidator validator,
        ICpfProtectionService cpfProtectionService,
        IClienteIdentityService identityService,
        IClienteRepository clienteRepository,
        IEmailConfirmationOutbox emailConfirmationOutbox,
        IClienteTransactionFactory transactionFactory)
    {
        _validator = validator;
        _cpfProtectionService = cpfProtectionService;
        _identityService = identityService;
        _clienteRepository = clienteRepository;
        _emailConfirmationOutbox = emailConfirmationOutbox;
        _transactionFactory = transactionFactory;
    }

    public async Task<CadastroClienteResult> ExecuteAsync(
        CadastroClienteInput? input,
        CancellationToken cancellationToken = default)
    {
        var failureCategory = CadastroClienteFailureCategory.CONFIGURATION_INFRASTRUCTURE;
        var failureStage = CadastroClienteFailureStage.USE_CASE_ENTRY;

        try
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

            failureCategory = CadastroClienteFailureCategory.CONFIGURATION_INFRASTRUCTURE;
            failureStage = CadastroClienteFailureStage.CPF_PROTECTION;
            var fingerprint = _cpfProtectionService.CreateFingerprintForLookup(cpf);
            var protectedCpf = _cpfProtectionService.Protect(cpf);
            if (!string.Equals(fingerprint, protectedCpf.Fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A proteção do CPF não produziu uma impressão digital consistente.");
            }

            failureCategory = CadastroClienteFailureCategory.IDENTITY;
            failureStage = CadastroClienteFailureStage.ROLE_LOOKUP;
            if (!await _identityService.RoleExistsAsync(RoleCliente, cancellationToken).ConfigureAwait(false))
            {
                return CadastroClienteResult.Failure(
                    CadastroClienteStatus.RoleUnavailable,
                    failureCategory,
                    failureStage);
            }

            failureCategory = CadastroClienteFailureCategory.PERSISTENCE_TRANSACTION;
            failureStage = CadastroClienteFailureStage.TRANSACTION_BEGIN;
            var userId = Guid.NewGuid();
            await using var transaction = await _transactionFactory
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            failureCategory = CadastroClienteFailureCategory.IDENTITY;
            failureStage = CadastroClienteFailureStage.IDENTITY_USER_CREATE;
            var identityResult = await _identityService.CreateUserAsync(
                userId,
                input.Email!.Trim(),
                input.Telefone!.Trim(),
                input.Senha!,
                cancellationToken).ConfigureAwait(false);

            if (!identityResult.Succeeded)
            {
                return identityResult.Status switch
                {
                    ClienteIdentityOperationStatus.DuplicateEmail =>
                        CadastroClienteResult.Failure(
                            CadastroClienteStatus.EmailAlreadyRegistered,
                            failureCategory,
                            failureStage),
                    ClienteIdentityOperationStatus.PasswordValidationFailed =>
                        CadastroClienteResult.Failure(
                            CadastroClienteStatus.PasswordValidationFailed,
                            failureCategory,
                            failureStage,
                            identityResult.SafeDiagnosticCodes),
                    ClienteIdentityOperationStatus.PersistenceFailed =>
                        CadastroClienteResult.Failure(
                            CadastroClienteStatus.PersistenceFailed,
                            CadastroClienteFailureCategory.PERSISTENCE_TRANSACTION,
                            CadastroClienteFailureStage.IDENTITY_USER_PERSISTENCE),
                    _ => CadastroClienteResult.Failure(
                        CadastroClienteStatus.IdentityCreationFailed,
                        failureCategory,
                        failureStage,
                        identityResult.SafeDiagnosticCodes)
                };
            }

            failureStage = CadastroClienteFailureStage.IDENTITY_ROLE_ASSIGNMENT;
            var roleResult = await _identityService.AddToRoleAsync(
                userId,
                RoleCliente,
                cancellationToken).ConfigureAwait(false);
            if (!roleResult.Succeeded)
            {
                return roleResult.Status == ClienteIdentityOperationStatus.PersistenceFailed
                    ? CadastroClienteResult.Failure(
                        CadastroClienteStatus.PersistenceFailed,
                        CadastroClienteFailureCategory.PERSISTENCE_TRANSACTION,
                        CadastroClienteFailureStage.IDENTITY_ROLE_PERSISTENCE)
                    : CadastroClienteResult.Failure(
                        CadastroClienteStatus.RoleAssignmentFailed,
                        failureCategory,
                        failureStage);
            }

            failureCategory = CadastroClienteFailureCategory.PROFILE_ADDRESS;
            failureStage = CadastroClienteFailureStage.PROFILE_CREATE;
            var profile = ClientePerfil.Criar(userId, input.NomeCompleto, protectedCpf);
            var addressInput = input.EnderecoInicial!;

            failureStage = CadastroClienteFailureStage.ADDRESS_CREATE;
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

            failureStage = CadastroClienteFailureStage.PROFILE_ADDRESS_PERSISTENCE;
            _clienteRepository.Adicionar(profile);
            _clienteRepository.Adicionar(address);

            failureCategory = CadastroClienteFailureCategory.PERSISTENCE_TRANSACTION;
            failureStage = CadastroClienteFailureStage.OUTBOX_ENQUEUE;
            _emailConfirmationOutbox.EnqueueEmailConfirmation(userId);

            failureStage = CadastroClienteFailureStage.TRANSACTION_SAVE;
            await transaction.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            failureStage = CadastroClienteFailureStage.TRANSACTION_COMMIT;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return CadastroClienteResult.Success(userId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ClienteCpfDuplicadoException or ClientePersistenceException)
        {
            return exception is ClienteCpfDuplicadoException
                ? CadastroClienteResult.Failure(
                    CadastroClienteStatus.CpfAlreadyRegistered,
                    failureCategory,
                    failureStage)
                : CadastroClienteResult.Failure(
                    CadastroClienteStatus.PersistenceFailed,
                    failureCategory,
                    failureStage);
        }
        catch (Exception exception)
        {
            CadastroClienteDiagnostics.Attach(exception, failureCategory, failureStage);
            throw;
        }
    }
}
