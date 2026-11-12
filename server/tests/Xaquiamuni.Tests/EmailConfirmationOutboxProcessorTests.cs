using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests;

public sealed class EmailConfirmationOutboxProcessorTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessAsync_SendsCurrentConfirmationAndCompletesOnlyAfterSuccess()
    {
        var claim = CreateClaim();
        var store = new FakeOutboxStore();
        var confirmation = new FakeEmailConfirmationService(
            EmailConfirmationTokenResult.GeneratedForUser(
                "current@example.invalid",
                "private-confirmation-token"));
        var composer = new FakeMessageComposer();
        var sender = new FakeEmailSender();
        var processor = CreateProcessor(store, confirmation, composer, sender);

        Assert.Equal(EmailOutboxMessageType.EmailConfirmation, processor.MessageType);
        await processor.ProcessAsync(claim);

        Assert.Equal(claim.UserId, confirmation.LastUserId);
        Assert.Equal(1, confirmation.GenerateCalls);
        Assert.Equal(EmailConfirmationTokenStatus.Generated, composer.LastResult?.Status);
        Assert.Equal(1, sender.SendCalls);
        Assert.Same(composer.Message, sender.LastMessage);
        Assert.Equal(new[] { "Complete" }, store.Operations);
        Assert.Same(claim, store.LastClaim);
        Assert.DoesNotContain("private-confirmation-token", sender.LastMessage!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EmailConfirmationTokenStatus.UserNotFound)]
    [InlineData(EmailConfirmationTokenStatus.EmailMissing)]
    [InlineData(EmailConfirmationTokenStatus.AlreadyConfirmed)]
    public async Task ProcessAsync_CancelsWhenUserCannotReceiveConfirmation(
        EmailConfirmationTokenStatus status)
    {
        var claim = CreateClaim();
        var store = new FakeOutboxStore();
        var confirmation = new FakeEmailConfirmationService(CreateResult(status));
        var composer = new FakeMessageComposer();
        var sender = new FakeEmailSender();
        var processor = CreateProcessor(store, confirmation, composer, sender);

        await processor.ProcessAsync(claim);

        Assert.Equal(new[] { "Cancel" }, store.Operations);
        Assert.Same(claim, store.LastClaim);
        Assert.Equal(0, composer.ComposeCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    [Fact]
    public async Task ProcessAsync_RetriesTransientSendFailureUsingRetryPolicy()
    {
        var claim = CreateClaim(attempts: 2);
        var store = new FakeOutboxStore();
        var confirmation = GeneratedConfirmation();
        var composer = new FakeMessageComposer();
        var sender = new FakeEmailSender(new EmailDeliveryException(isTransient: true));
        var processor = CreateProcessor(store, confirmation, composer, sender);

        await processor.ProcessAsync(claim);

        Assert.Equal(new[] { "Retry" }, store.Operations);
        Assert.Same(claim, store.LastClaim);
        Assert.InRange(store.LastJitterSample, 0, 1);
        Assert.Equal(0, store.CompleteCalls);
    }

    [Fact]
    public async Task ProcessAsync_DeadLettersPermanentFailureAndRetryLimit()
    {
        var permanentClaim = CreateClaim();
        var permanentStore = new FakeOutboxStore();
        var permanentProcessor = CreateProcessor(
            permanentStore,
            GeneratedConfirmation(),
            new FakeMessageComposer(),
            new FakeEmailSender(new EmailDeliveryException(isTransient: false)));

        await permanentProcessor.ProcessAsync(permanentClaim);

        Assert.Equal(new[] { "DeadLetter" }, permanentStore.Operations);

        var exhaustedClaim = CreateClaim(attempts: EmailOutboxRetryPolicy.MaximumAttempts);
        var exhaustedStore = new FakeOutboxStore();
        var exhaustedProcessor = CreateProcessor(
            exhaustedStore,
            GeneratedConfirmation(),
            new FakeMessageComposer(),
            new FakeEmailSender(new EmailDeliveryException(isTransient: true)));

        await exhaustedProcessor.ProcessAsync(exhaustedClaim);

        Assert.Equal(new[] { "DeadLetter" }, exhaustedStore.Operations);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotRetryOrChangeStateWhenCompletionLosesConcurrencyRace()
    {
        var claim = CreateClaim();
        var store = new FakeOutboxStore { CompleteResult = false };
        var sender = new FakeEmailSender();
        var processor = CreateProcessor(
            store,
            GeneratedConfirmation(),
            new FakeMessageComposer(),
            sender);

        await processor.ProcessAsync(claim);

        Assert.Equal(1, sender.SendCalls);
        Assert.Equal(new[] { "Complete" }, store.Operations);
        Assert.Same(claim, store.LastClaim);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotProcessClaimWithExpiredLease()
    {
        var claim = CreateClaim(leaseExpiresAtUtc: UtcNow.AddTicks(-1));
        var store = new FakeOutboxStore();
        var confirmation = GeneratedConfirmation();
        var sender = new FakeEmailSender();
        var processor = CreateProcessor(
            store,
            confirmation,
            new FakeMessageComposer(),
            sender);

        await processor.ProcessAsync(claim);

        Assert.Empty(store.Operations);
        Assert.Equal(0, confirmation.GenerateCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    [Fact]
    public async Task ProcessAsync_RejectsPasswordResetClaimWithoutProcessingIt()
    {
        var claim = CreateClaim(messageType: EmailOutboxMessageType.PasswordReset);
        var store = new FakeOutboxStore();
        var confirmation = GeneratedConfirmation();
        var composer = new FakeMessageComposer();
        var sender = new FakeEmailSender();
        var processor = CreateProcessor(store, confirmation, composer, sender);

        await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(claim));

        Assert.Empty(store.Operations);
        Assert.Equal(0, confirmation.GenerateCalls);
        Assert.Equal(0, composer.ComposeCalls);
        Assert.Equal(0, sender.SendCalls);
    }

    private static EmailConfirmationOutboxProcessor CreateProcessor(
        FakeOutboxStore store,
        FakeEmailConfirmationService confirmation,
        FakeMessageComposer composer,
        FakeEmailSender sender) =>
        new(store, confirmation, composer, sender, new FixedTimeProvider(UtcNow));

    private static FakeEmailConfirmationService GeneratedConfirmation() =>
        new(EmailConfirmationTokenResult.GeneratedForUser(
            "current@example.invalid",
            "private-confirmation-token"));

    private static EmailConfirmationTokenResult CreateResult(EmailConfirmationTokenStatus status) =>
        status switch
        {
            EmailConfirmationTokenStatus.UserNotFound => EmailConfirmationTokenResult.UserNotFound(),
            EmailConfirmationTokenStatus.EmailMissing => EmailConfirmationTokenResult.EmailMissing(),
            EmailConfirmationTokenStatus.AlreadyConfirmed => EmailConfirmationTokenResult.AlreadyConfirmed(),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static EmailOutboxClaim CreateClaim(
        int attempts = 1,
        DateTimeOffset? leaseExpiresAtUtc = null,
        EmailOutboxMessageType messageType = EmailOutboxMessageType.EmailConfirmation) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            messageType,
            attempts,
            leaseExpiresAtUtc ?? UtcNow.AddMinutes(2),
            [1, 2, 3, 4, 5, 6, 7, 8]);

    private sealed class FakeOutboxStore : IEmailOutboxStore
    {
        public List<string> Operations { get; } = [];
        public EmailOutboxClaim? LastClaim { get; private set; }
        public int CompleteCalls { get; private set; }
        public bool CompleteResult { get; init; } = true;
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
            CompleteCalls++;
            return Task.FromResult(CompleteResult);
        }

        public Task<bool> CancelAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("Cancel");
            LastClaim = claim;
            return Task.FromResult(true);
        }

        public Task<bool> RetryAsync(
            EmailOutboxClaim claim,
            double jitterSample,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("Retry");
            LastClaim = claim;
            LastJitterSample = jitterSample;
            return Task.FromResult(true);
        }

        public Task<bool> DeadLetterAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("DeadLetter");
            LastClaim = claim;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeEmailConfirmationService(EmailConfirmationTokenResult result)
        : IEmailConfirmationService
    {
        public int GenerateCalls { get; private set; }
        public Guid LastUserId { get; private set; }

        public Task<Guid?> FindUnconfirmedUserIdByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            LastUserId = userId;
            return Task.FromResult(result);
        }

        public Task<EmailConfirmationResult> ConfirmEmailAsync(
            string email,
            string token,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMessageComposer : IEmailConfirmationMessageComposer
    {
        public EmailMessage Message { get; } = new(
            "current@example.invalid",
            "Confirm your email",
            "Confirmation content");
        public int ComposeCalls { get; private set; }
        public EmailConfirmationTokenResult? LastResult { get; private set; }

        public EmailMessage Compose(EmailConfirmationTokenResult tokenResult)
        {
            ComposeCalls++;
            LastResult = tokenResult;
            return Message;
        }
    }

    private sealed class FakeEmailSender(Exception? exception = null) : IEmailSender
    {
        public int SendCalls { get; private set; }
        public EmailMessage? LastMessage { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            LastMessage = message;
            return exception is null
                ? Task.CompletedTask
                : Task.FromException(exception);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
