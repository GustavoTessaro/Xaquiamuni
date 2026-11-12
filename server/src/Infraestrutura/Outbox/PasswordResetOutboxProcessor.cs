using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Infraestrutura.Outbox;

public sealed class PasswordResetOutboxProcessor : IEmailOutboxProcessor
{
    private const int CredentialGenerationFingerprintLength = 32;
    private readonly IEmailOutboxStore _outboxStore;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IPasswordResetMessageComposer _messageComposer;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _timeProvider;

    public PasswordResetOutboxProcessor(
        IEmailOutboxStore outboxStore,
        IPasswordResetService passwordResetService,
        IPasswordResetMessageComposer messageComposer,
        IEmailSender emailSender,
        TimeProvider timeProvider)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _passwordResetService = passwordResetService
            ?? throw new ArgumentNullException(nameof(passwordResetService));
        _messageComposer = messageComposer ?? throw new ArgumentNullException(nameof(messageComposer));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public EmailOutboxMessageType MessageType => EmailOutboxMessageType.PasswordReset;

    public async Task ProcessAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.MessageType != EmailOutboxMessageType.PasswordReset)
        {
            throw new ArgumentException(
                "The password reset processor only accepts PasswordReset claims.",
                nameof(claim));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!HasActiveLease(claim))
        {
            return;
        }

        var credentialGenerationFingerprint = claim.CredentialGenerationFingerprint;
        if (credentialGenerationFingerprint is not ReadOnlyMemory<byte> fingerprint
            || fingerprint.Length != CredentialGenerationFingerprintLength)
        {
            await _outboxStore.DeadLetterAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }

        PasswordResetTokenResult? tokenResult;
        try
        {
            tokenResult = await _passwordResetService
                .GeneratePasswordResetTokenForUserAsync(
                    claim.UserId,
                    fingerprint,
                    cancellationToken)
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
        if (tokenResult is null)
        {
            await _outboxStore.DeadLetterAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (tokenResult.Status)
        {
            case PasswordResetTokenStatus.NotEligible:
            case PasswordResetTokenStatus.CredentialGenerationMismatch:
                await _outboxStore.CancelAsync(claim, cancellationToken).ConfigureAwait(false);
                return;
            case PasswordResetTokenStatus.InvalidRequest:
                await _outboxStore.DeadLetterAsync(claim, cancellationToken).ConfigureAwait(false);
                return;
            case PasswordResetTokenStatus.Generated:
                break;
            default:
                await _outboxStore.DeadLetterAsync(claim, cancellationToken).ConfigureAwait(false);
                return;
        }

        EmailMessage message;
        try
        {
            message = _messageComposer.Compose(tokenResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or FormatException)
        {
            await _outboxStore.DeadLetterAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ScheduleRetryAsync(claim, cancellationToken).ConfigureAwait(false);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
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
