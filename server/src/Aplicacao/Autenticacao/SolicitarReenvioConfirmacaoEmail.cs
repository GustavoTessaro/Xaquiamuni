using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class SolicitarReenvioConfirmacaoEmail : ISolicitarReenvioConfirmacaoEmail
{
    private readonly CadastroClienteInputValidator _validator;
    private readonly IEmailConfirmationService _emailConfirmationService;
    private readonly IEmailConfirmationResendOutbox _emailConfirmationResendOutbox;

    public SolicitarReenvioConfirmacaoEmail(
        CadastroClienteInputValidator validator,
        IEmailConfirmationService emailConfirmationService,
        IEmailConfirmationResendOutbox emailConfirmationResendOutbox)
    {
        _validator = validator;
        _emailConfirmationService = emailConfirmationService;
        _emailConfirmationResendOutbox = emailConfirmationResendOutbox;
    }

    public async Task<SolicitarReenvioConfirmacaoEmailResult> ExecuteAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_validator.ValidateEmail(email).Valido)
        {
            return SolicitarReenvioConfirmacaoEmailResult.InvalidEmail();
        }

        var normalizedEmail = email!.Trim();
        var userId = await _emailConfirmationService
            .FindUnconfirmedUserIdByEmailAsync(normalizedEmail, cancellationToken)
            .ConfigureAwait(false);
        if (userId is null)
        {
            return SolicitarReenvioConfirmacaoEmailResult.RequestProcessed();
        }

        var outboxResult = await _emailConfirmationResendOutbox
            .RequestAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        return outboxResult switch
        {
            EmailConfirmationResendOutboxResult.Enqueued
                or EmailConfirmationResendOutboxResult.NotEligible
                or EmailConfirmationResendOutboxResult.AlreadyQueued
                or EmailConfirmationResendOutboxResult.CooldownActive
                or EmailConfirmationResendOutboxResult.HourlyLimitReached =>
                    SolicitarReenvioConfirmacaoEmailResult.RequestProcessed(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outboxResult),
                "The email confirmation resend outbox returned an unsupported result.")
        };
    }
}
