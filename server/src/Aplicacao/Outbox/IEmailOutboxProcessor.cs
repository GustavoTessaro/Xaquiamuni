namespace Xaquiamuni.Aplicacao.Outbox;

public interface IEmailOutboxProcessor
{
    EmailOutboxMessageType MessageType { get; }

    Task ProcessAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default);
}
