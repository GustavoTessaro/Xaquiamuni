using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class SolicitarRecuperacaoSenha : ISolicitarRecuperacaoSenha
{
    private readonly CadastroClienteInputValidator _validator;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IPasswordResetOutbox _passwordResetOutbox;

    public SolicitarRecuperacaoSenha(
        CadastroClienteInputValidator validator,
        IPasswordResetService passwordResetService,
        IPasswordResetOutbox passwordResetOutbox)
    {
        _validator = validator;
        _passwordResetService = passwordResetService;
        _passwordResetOutbox = passwordResetOutbox;
    }

    public async Task<SolicitarRecuperacaoSenhaResult> ExecuteAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_validator.ValidateEmail(email).Valido)
        {
            return SolicitarRecuperacaoSenhaResult.InvalidEmail();
        }

        var normalizedEmail = email!.Trim();
        var userId = await _passwordResetService
            .FindConfirmedUserIdByEmailAsync(normalizedEmail, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (userId is null)
        {
            return SolicitarRecuperacaoSenhaResult.RequestProcessed();
        }

        var outboxResult = await _passwordResetOutbox
            .RequestAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return outboxResult switch
        {
            PasswordResetOutboxResult.Enqueued
                or PasswordResetOutboxResult.NotEligible
                or PasswordResetOutboxResult.AlreadyQueued
                or PasswordResetOutboxResult.CooldownActive
                or PasswordResetOutboxResult.HourlyLimitReached =>
                    SolicitarRecuperacaoSenhaResult.RequestProcessed(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outboxResult),
                "The password reset outbox returned an unsupported result.")
        };
    }
}
