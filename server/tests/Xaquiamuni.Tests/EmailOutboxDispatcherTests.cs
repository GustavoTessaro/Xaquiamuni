using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura.Outbox;

namespace Xaquiamuni.Tests;

public sealed class EmailOutboxDispatcherTests
{
    [Fact]
    public async Task ProcessAsync_DispatchesEachClaimToTheMatchingProcessor()
    {
        var confirmationProcessor = new RecordingProcessor(EmailOutboxMessageType.EmailConfirmation);
        var passwordResetProcessor = new RecordingProcessor(EmailOutboxMessageType.PasswordReset);
        var dispatcher = new EmailOutboxDispatcher(
            [passwordResetProcessor, confirmationProcessor]);

        await dispatcher.ProcessAsync(CreateClaim(EmailOutboxMessageType.EmailConfirmation));
        await dispatcher.ProcessAsync(CreateClaim(EmailOutboxMessageType.PasswordReset));

        Assert.Equal(
            [
                EmailOutboxMessageType.EmailConfirmation,
                EmailOutboxMessageType.PasswordReset
            ],
            dispatcher.SupportedMessageTypes);
        Assert.Single(confirmationProcessor.Claims);
        Assert.Equal(
            EmailOutboxMessageType.EmailConfirmation,
            confirmationProcessor.Claims[0].MessageType);
        Assert.Single(passwordResetProcessor.Claims);
        Assert.Equal(
            EmailOutboxMessageType.PasswordReset,
            passwordResetProcessor.Claims[0].MessageType);
    }

    [Fact]
    public async Task ProcessAsync_RejectsTypeWithoutProcessorWithoutDispatching()
    {
        var confirmationProcessor = new RecordingProcessor(EmailOutboxMessageType.EmailConfirmation);
        var dispatcher = new EmailOutboxDispatcher([confirmationProcessor]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.ProcessAsync(CreateClaim(EmailOutboxMessageType.PasswordReset)));

        Assert.Contains("No Outbox processor is registered", exception.Message, StringComparison.Ordinal);
        Assert.Empty(confirmationProcessor.Claims);
    }

    [Fact]
    public void Constructor_RejectsDuplicateProcessorsForOneMessageType()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new EmailOutboxDispatcher(
            [
                new RecordingProcessor(EmailOutboxMessageType.EmailConfirmation),
                new RecordingProcessor(EmailOutboxMessageType.EmailConfirmation)
            ]));

        Assert.Contains("More than one Outbox processor", exception.Message, StringComparison.Ordinal);
    }

    private static EmailOutboxClaim CreateClaim(EmailOutboxMessageType messageType) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            messageType,
            attempts: 1,
            DateTimeOffset.UtcNow.AddMinutes(2),
            [1, 2, 3, 4, 5, 6, 7, 8],
            messageType == EmailOutboxMessageType.PasswordReset ? new byte[32] : null);

    private sealed class RecordingProcessor(EmailOutboxMessageType messageType)
        : IEmailOutboxProcessor
    {
        public EmailOutboxMessageType MessageType => messageType;
        public List<EmailOutboxClaim> Claims { get; } = [];

        public Task ProcessAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default)
        {
            Claims.Add(claim);
            return Task.CompletedTask;
        }
    }
}
