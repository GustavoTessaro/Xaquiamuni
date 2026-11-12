using System.Reflection;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests;

public sealed class PasswordResetOutboxProcessorTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessAsync_GeneratesProtectedTokenSendsAndCompletesAfterAcceptance()
    {
        var claim = CreateClaim();
        var operations = new List<string>();
        var store = new FakeOutboxStore(operations);
        var passwordReset = new FakePasswordResetService(
            PasswordResetTokenResult.Generated("client@example.invalid", "synthetic-reset-token"),
            operations);
        var composer = new FakeMessageComposer(operations);
        var sender = new FakeEmailSender(operations);
        var processor = CreateProcessor(store, passwordReset, composer, sender);

        Assert.Equal(EmailOutboxMessageType.PasswordReset, processor.MessageType);
        await processor.ProcessAsync(claim);

        Assert.Equal(claim.UserId, passwordReset.LastUserId);
        Assert.Equal(claim.CredentialGenerationFingerprint, passwordReset.LastFingerprint);
        Assert.Equal(1, passwordReset.GenerateCalls);
        Assert.Equal(0, passwordReset.FindByEmailCalls);
        Assert.Equal(PasswordResetTokenStatus.Generated, composer.LastResult?.Status);
        Assert.Equal(1, sender.SendCalls);
        Assert.Same(composer.Message, sender.LastMessage);
        Assert.Equal(["Generate", "Compose", "Send", "Complete"], operations);
        Assert.Same(claim, store.LastClaim);
        Assert.DoesNotContain(
            "synthetic-reset-token",
            composer.LastResult!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_RejectsWrongMessageTypeBeforeGenerationOrStateChanges()
    {
        var claim = CreateClaim(EmailOutboxMessageType.EmailConfirmation);
        var store = new FakeOutboxStore([]);
        var passwordReset = new FakePasswordResetService(GeneratedResult());
        var composer = new FakeMessageComposer([]);
        var sender = new FakeEmailSender([]);
        var processor = CreateProcessor(store, passwordReset, composer, sender);

        await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(claim));

        Assert.Equal(0, passwordReset.GenerateCalls);
        Assert.Equal(0, composer.ComposeCalls);
        Assert.Equal(0, sender.SendCalls);
        Assert.Empty(store.Operations);
    }

    [Theory]
    [InlineData(PasswordResetTokenStatus.NotEligible, "Cancel")]
    [InlineData(PasswordResetTokenStatus.CredentialGenerationMismatch, "Cancel")]
    [InlineData(PasswordResetTokenStatus.InvalidRequest, "DeadLetter")]
    public async Task ProcessAsync_MapsProtectedGenerationResultsToExpectedState(
        PasswordResetTokenStatus status,
        string expectedOperation)
    {
        var claim = CreateClaim();
        var store = new FakeOutboxStore([]);
        var passwordReset = new FakePasswordResetService(CreateResult(status));
        var composer = new FakeMessageComposer([]);
        var sender = new FakeEmailSender([]);
        var processor = CreateProcessor(store, passwordReset, composer, sender);

        await processor.ProcessAsync(claim);

        Assert.Equal([expectedOperation], store.Operations);
        Assert.Same(claim, store.LastClaim);
        Assert.Equal(0, composer.ComposeCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(31)]
    public async Task ProcessAsync_DeadLettersMissingOrInvalidFingerprintWithoutGeneration(int? fingerprintLength)
    {
        var claim = CreateClaim();
        SetFingerprint(claim, fingerprintLength is null ? null : new byte[fingerprintLength.Value]);
        var store = new FakeOutboxStore([]);
        var passwordReset = new FakePasswordResetService(GeneratedResult());
        var composer = new FakeMessageComposer([]);
        var sender = new FakeEmailSender([]);
        var processor = CreateProcessor(store, passwordReset, composer, sender);

        await processor.ProcessAsync(claim);

        Assert.Equal(["DeadLetter"], store.Operations);
        Assert.Equal(0, passwordReset.GenerateCalls);
        Assert.Equal(0, composer.ComposeCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    [Fact]
    public async Task ProcessAsync_RetriesTransientFailureAndDeadLettersPermanentFailureOrExhaustion()
    {
        var transientClaim = CreateClaim(attempts: 2);
        var transientStore = new FakeOutboxStore([]);
        var transientProcessor = CreateProcessor(
            transientStore,
            new FakePasswordResetService(GeneratedResult()),
            new FakeMessageComposer([]),
            new FakeEmailSender([], new EmailDeliveryException(isTransient: true)));

        await transientProcessor.ProcessAsync(transientClaim);

        Assert.Equal(["Retry"], transientStore.Operations);
        Assert.InRange(transientStore.LastJitterSample, 0, 1);
        Assert.Same(transientClaim, transientStore.LastClaim);

        var permanentStore = new FakeOutboxStore([]);
        var permanentProcessor = CreateProcessor(
            permanentStore,
            new FakePasswordResetService(GeneratedResult()),
            new FakeMessageComposer([]),
            new FakeEmailSender([], new EmailDeliveryException(isTransient: false)));

        await permanentProcessor.ProcessAsync(CreateClaim());
        Assert.Equal(["DeadLetter"], permanentStore.Operations);

        var exhaustedStore = new FakeOutboxStore([]);
        var exhaustedProcessor = CreateProcessor(
            exhaustedStore,
            new FakePasswordResetService(GeneratedResult()),
            new FakeMessageComposer([]),
            new FakeEmailSender([], new EmailDeliveryException(isTransient: true)));

        await exhaustedProcessor.ProcessAsync(
            CreateClaim(attempts: EmailOutboxRetryPolicy.MaximumAttempts));
        Assert.Equal(["DeadLetter"], exhaustedStore.Operations);
    }

    [Fact]
    public async Task ProcessAsync_RetriesUnexpectedGenerationAndSendFailures()
    {
        var generationStore = new FakeOutboxStore([]);
        var generationProcessor = CreateProcessor(
            generationStore,
            new FakePasswordResetService(exception: new InvalidOperationException("synthetic failure")),
            new FakeMessageComposer([]),
            new FakeEmailSender([]));

        await generationProcessor.ProcessAsync(CreateClaim());
        Assert.Equal(["Retry"], generationStore.Operations);

        var sendStore = new FakeOutboxStore([]);
        var sendProcessor = CreateProcessor(
            sendStore,
            new FakePasswordResetService(GeneratedResult()),
            new FakeMessageComposer([]),
            new FakeEmailSender([], new InvalidOperationException("synthetic failure")));

        await sendProcessor.ProcessAsync(CreateClaim());
        Assert.Equal(["Retry"], sendStore.Operations);
    }

    [Fact]
    public async Task ProcessAsync_DeadLettersCompositionFailuresWithoutSending()
    {
        var store = new FakeOutboxStore([]);
        var composer = new FakeMessageComposer([], new ArgumentException("invalid synthetic message"));
        var sender = new FakeEmailSender([]);
        var processor = CreateProcessor(
            store,
            new FakePasswordResetService(GeneratedResult()),
            composer,
            sender);

        await processor.ProcessAsync(CreateClaim());

        Assert.Equal(["DeadLetter"], store.Operations);
        Assert.Equal(1, composer.ComposeCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    [Fact]
    public async Task ProcessAsync_RetriesUnexpectedCompositionFailureWithoutExposingException()
    {
        var store = new FakeOutboxStore([]);
        var sender = new FakeEmailSender([]);
        var secret = "synthetic-reset-token";
        var processor = CreateProcessor(
            store,
            new FakePasswordResetService(PasswordResetTokenResult.Generated("client@example.invalid", secret)),
            new FakeMessageComposer([], new NotSupportedException("unsupported synthetic composer")),
            sender);

        await processor.ProcessAsync(CreateClaim());

        Assert.Equal(["Retry"], store.Operations);
        Assert.Equal(0, sender.SendCalls);
    }

    [Fact]
    public async Task ProcessAsync_PropagatesCallerCancellationWithoutChangingState()
    {
        var claim = CreateClaim();
        using var cancellationSource = new CancellationTokenSource();
        var store = new FakeOutboxStore([]);
        var passwordReset = new FakePasswordResetService(
            exception: new OperationCanceledException(cancellationSource.Token),
            operations: null);
        var processor = CreateProcessor(
            store,
            passwordReset,
            new FakeMessageComposer([]),
            new FakeEmailSender([]));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(claim, cancellationSource.Token));

        Assert.Empty(store.Operations);
        Assert.Equal(0, passwordReset.GenerateCalls);
    }

    [Fact]
    public async Task ProcessAsync_PropagatesCancellationFromSenderWithoutCompleting()
    {
        using var cancellationSource = new CancellationTokenSource();
        var store = new FakeOutboxStore([]);
        var sender = new FakeEmailSender([], onSend: cancellationSource.Cancel);
        var processor = CreateProcessor(
            store,
            new FakePasswordResetService(GeneratedResult()),
            new FakeMessageComposer([]),
            sender);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(CreateClaim(), cancellationSource.Token));

        Assert.Equal(1, sender.SendCalls);
        Assert.Empty(store.Operations);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotSendOrChangeStateWhenLeaseExpiresAfterComposition()
    {
        var timeProvider = new MutableTimeProvider(UtcNow);
        var store = new FakeOutboxStore([]);
        var composer = new FakeMessageComposer([], onCompose: () =>
            timeProvider.SetUtcNow(UtcNow.AddMinutes(3)));
        var sender = new FakeEmailSender([]);
        var processor = CreateProcessor(
            store,
            new FakePasswordResetService(GeneratedResult()),
            composer,
            sender,
            timeProvider);

        await processor.ProcessAsync(CreateClaim(leaseExpiresAtUtc: UtcNow.AddMinutes(2)));

        Assert.Empty(store.Operations);
        Assert.Equal(1, composer.ComposeCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotRetryOrOverwriteAfterConditionalTransitionLosesRace()
    {
        var completionStore = new FakeOutboxStore([]) { CompleteResult = false };
        var sender = new FakeEmailSender([]);
        var processor = CreateProcessor(
            completionStore,
            new FakePasswordResetService(GeneratedResult()),
            new FakeMessageComposer([]),
            sender);

        await processor.ProcessAsync(CreateClaim());

        Assert.Equal(1, sender.SendCalls);
        Assert.Equal(["Complete"], completionStore.Operations);

        var cancellationStore = new FakeOutboxStore([]) { CancelResult = false };
        var cancellationProcessor = CreateProcessor(
            cancellationStore,
            new FakePasswordResetService(PasswordResetTokenResult.NotEligible()),
            new FakeMessageComposer([]),
            new FakeEmailSender([]));

        await cancellationProcessor.ProcessAsync(CreateClaim());
        Assert.Equal(["Cancel"], cancellationStore.Operations);

        var deadLetterStore = new FakeOutboxStore([]) { DeadLetterResult = false };
        var deadLetterProcessor = CreateProcessor(
            deadLetterStore,
            new FakePasswordResetService(PasswordResetTokenResult.InvalidRequest()),
            new FakeMessageComposer([]),
            new FakeEmailSender([]));

        await deadLetterProcessor.ProcessAsync(CreateClaim());
        Assert.Equal(["DeadLetter"], deadLetterStore.Operations);

        var retryStore = new FakeOutboxStore([]) { RetryResult = false };
        var retryProcessor = CreateProcessor(
            retryStore,
            new FakePasswordResetService(GeneratedResult()),
            new FakeMessageComposer([]),
            new FakeEmailSender([], new EmailDeliveryException(isTransient: true)));

        await retryProcessor.ProcessAsync(CreateClaim());
        Assert.Equal(["Retry"], retryStore.Operations);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotProcessExpiredLease()
    {
        var store = new FakeOutboxStore([]);
        var passwordReset = new FakePasswordResetService(GeneratedResult());
        var sender = new FakeEmailSender([]);
        var processor = CreateProcessor(
            store,
            passwordReset,
            new FakeMessageComposer([]),
            sender);

        await processor.ProcessAsync(CreateClaim(leaseExpiresAtUtc: UtcNow.AddTicks(-1)));

        Assert.Empty(store.Operations);
        Assert.Equal(0, passwordReset.GenerateCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    private static PasswordResetOutboxProcessor CreateProcessor(
        FakeOutboxStore store,
        FakePasswordResetService passwordReset,
        FakeMessageComposer composer,
        FakeEmailSender sender,
        TimeProvider? timeProvider = null) =>
        new(
            store,
            passwordReset,
            composer,
            sender,
            timeProvider ?? new FixedTimeProvider(UtcNow));

    private static PasswordResetTokenResult GeneratedResult() =>
        PasswordResetTokenResult.Generated("client@example.invalid", "synthetic-reset-token");

    private static PasswordResetTokenResult CreateResult(PasswordResetTokenStatus status) =>
        status switch
        {
            PasswordResetTokenStatus.NotEligible => PasswordResetTokenResult.NotEligible(),
            PasswordResetTokenStatus.CredentialGenerationMismatch =>
                PasswordResetTokenResult.CredentialGenerationMismatch(),
            PasswordResetTokenStatus.InvalidRequest => PasswordResetTokenResult.InvalidRequest(),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static EmailOutboxClaim CreateClaim(
        EmailOutboxMessageType messageType = EmailOutboxMessageType.PasswordReset,
        int attempts = 1,
        DateTimeOffset? leaseExpiresAtUtc = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            messageType,
            attempts,
            leaseExpiresAtUtc ?? UtcNow.AddMinutes(2),
            [1, 2, 3, 4, 5, 6, 7, 8],
            messageType == EmailOutboxMessageType.PasswordReset ? new byte[32] : null);

    private static void SetFingerprint(EmailOutboxClaim claim, byte[]? fingerprint) =>
        typeof(EmailOutboxClaim)
            .GetField("_credentialGenerationFingerprint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(claim, fingerprint);

    private sealed class FakeOutboxStore(List<string> operations) : IEmailOutboxStore
    {
        public List<string> Operations { get; } = operations;
        public EmailOutboxClaim? LastClaim { get; private set; }
        public bool CompleteResult { get; init; } = true;
        public bool CancelResult { get; init; } = true;
        public bool DeadLetterResult { get; init; } = true;
        public bool RetryResult { get; init; } = true;
        public double LastJitterSample { get; private set; }

        public Task<IReadOnlyList<EmailOutboxClaim>> ClaimDueAsync(
            EmailOutboxMessageType messageType,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedMessageIds = null) =>
            throw new NotSupportedException();

        public Task<bool> CompleteAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("Complete");
            LastClaim = claim;
            return Task.FromResult(CompleteResult);
        }

        public Task<bool> CancelAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("Cancel");
            LastClaim = claim;
            return Task.FromResult(CancelResult);
        }

        public Task<bool> RetryAsync(
            EmailOutboxClaim claim,
            double jitterSample,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("Retry");
            LastClaim = claim;
            LastJitterSample = jitterSample;
            return Task.FromResult(RetryResult);
        }

        public Task<bool> DeadLetterAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("DeadLetter");
            LastClaim = claim;
            return Task.FromResult(DeadLetterResult);
        }
    }

    private sealed class FakePasswordResetService : IPasswordResetService
    {
        private readonly PasswordResetTokenResult? _result;
        private readonly Exception? _exception;
        private readonly List<string>? _operations;

        public FakePasswordResetService(
            PasswordResetTokenResult? result = null,
            List<string>? operations = null,
            Exception? exception = null)
        {
            _result = result;
            _operations = operations;
            _exception = exception;
        }

        public Guid LastUserId { get; private set; }
        public ReadOnlyMemory<byte>? LastFingerprint { get; private set; }
        public int GenerateCalls { get; private set; }
        public int FindByEmailCalls { get; private set; }

        public Task<PasswordResetTokenResult> GeneratePasswordResetTokenForUserAsync(
            Guid userId,
            ReadOnlyMemory<byte>? credentialGenerationFingerprint,
            CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            LastUserId = userId;
            LastFingerprint = credentialGenerationFingerprint;
            _operations?.Add("Generate");
            return _exception is not null
                ? Task.FromException<PasswordResetTokenResult>(_exception)
                : Task.FromResult(_result!);
        }

        public Task<Guid?> FindConfirmedUserIdByEmailAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            FindByEmailCalls++;
            throw new NotSupportedException();
        }

        public Task<PasswordResetResult> ResetPasswordAsync(
            string? email,
            string? token,
            string? newPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMessageComposer : IPasswordResetMessageComposer
    {
        private readonly List<string> _operations;
        private readonly Exception? _exception;
        private readonly Action? _onCompose;

        public FakeMessageComposer(
            List<string> operations,
            Exception? exception = null,
            Action? onCompose = null)
        {
            _operations = operations;
            _exception = exception;
            _onCompose = onCompose;
        }

        public EmailMessage Message { get; } = new(
            "client@example.invalid",
            "Password reset",
            "Synthetic reset message");
        public int ComposeCalls { get; private set; }
        public PasswordResetTokenResult? LastResult { get; private set; }

        public EmailMessage Compose(PasswordResetTokenResult tokenResult)
        {
            ComposeCalls++;
            LastResult = tokenResult;
            _operations.Add("Compose");
            _onCompose?.Invoke();
            if (_exception is not null)
            {
                throw _exception;
            }

            return Message;
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        private readonly List<string> _operations;
        private readonly Exception? _exception;
        private readonly Action? _onSend;

        public FakeEmailSender(
            List<string> operations,
            Exception? exception = null,
            Action? onSend = null)
        {
            _operations = operations;
            _exception = exception;
            _onSend = onSend;
        }

        public int SendCalls { get; private set; }
        public EmailMessage? LastMessage { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            LastMessage = message;
            _operations.Add("Send");
            _onSend?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return _exception is null
                ? Task.CompletedTask
                : Task.FromException(_exception);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
