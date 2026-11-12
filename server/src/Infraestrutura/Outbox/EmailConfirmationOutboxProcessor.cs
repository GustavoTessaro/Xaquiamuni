using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Infraestrutura.Outbox;

public sealed class EmailConfirmationOutboxProcessor
{
    private readonly IEmailOutboxStore _outboxStore;
    private readonly IEmailConfirmationService _emailConfirmationService;
    private readonly IEmailConfirmationMessageComposer _messageComposer;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _timeProvider;

    public EmailConfirmationOutboxProcessor(
        IEmailOutboxStore outboxStore,
        IEmailConfirmationService emailConfirmationService,
        IEmailConfirmationMessageComposer messageComposer,
        IEmailSender emailSender,
        TimeProvider timeProvider)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _emailConfirmationService = emailConfirmationService
            ?? throw new ArgumentNullException(nameof(emailConfirmationService));
        _messageComposer = messageComposer ?? throw new ArgumentNullException(nameof(messageComposer));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task ProcessAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.MessageType != EmailOutboxMessageType.EmailConfirmation)
        {
            throw new ArgumentException(
                "The email confirmation processor only accepts EmailConfirmation claims.",
                nameof(claim));
        }

        if (!HasActiveLease(claim))
        {
            return;
        }

        EmailConfirmationTokenResult tokenResult;
        try
        {
            tokenResult = await _emailConfirmationService
                .GenerateEmailConfirmationTokenForUserAsync(claim.UserId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ScheduleRetryAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (tokenResult.Status != EmailConfirmationTokenStatus.Generated)
        {
            await _outboxStore.CancelAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }

        EmailMessage message;
        try
        {
            message = _messageComposer.Compose(tokenResult);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or FormatException)
        {
            await _outboxStore.DeadLetterAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!HasActiveLease(claim))
        {
            return;
        }

        try
        {
            await _emailSender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EmailDeliveryException exception) when (!exception.IsTransient)
        {
            await _outboxStore.DeadLetterAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ScheduleRetryAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _outboxStore.CompleteAsync(claim, cancellationToken).ConfigureAwait(false);
    }

    private bool HasActiveLease(EmailOutboxClaim claim) =>
        claim.LeaseExpiresAtUtc > _timeProvider.GetUtcNow();

    private Task<bool> ScheduleRetryAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken) =>
        claim.Attempts >= EmailOutboxRetryPolicy.MaximumAttempts
            ? _outboxStore.DeadLetterAsync(claim, cancellationToken)
            : _outboxStore.RetryAsync(claim, Random.Shared.NextDouble(), cancellationToken);
}
