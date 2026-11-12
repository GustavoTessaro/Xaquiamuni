using Xaquiamuni.Aplicacao.Outbox;

namespace Xaquiamuni.Infraestrutura.Outbox;

public sealed class EmailOutboxDispatcher
{
    private readonly IReadOnlyDictionary<EmailOutboxMessageType, IEmailOutboxProcessor> _processors;

    public EmailOutboxDispatcher(IEnumerable<IEmailOutboxProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);

        var processorMap = new Dictionary<EmailOutboxMessageType, IEmailOutboxProcessor>();
        foreach (var processor in processors)
        {
            ArgumentNullException.ThrowIfNull(processor);
            if (!Enum.IsDefined(processor.MessageType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(processors),
                    "An Outbox processor declares an unsupported message type.");
            }

            if (!processorMap.TryAdd(processor.MessageType, processor))
            {
                throw new InvalidOperationException(
                    $"More than one Outbox processor is registered for '{processor.MessageType}'.");
            }
        }

        _processors = processorMap;
        SupportedMessageTypes = Array.AsReadOnly(
            processorMap.Keys.OrderBy(messageType => (short)messageType).ToArray());
    }

    public IReadOnlyList<EmailOutboxMessageType> SupportedMessageTypes { get; }

    public Task ProcessAsync(
        EmailOutboxClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!_processors.TryGetValue(claim.MessageType, out var processor))
        {
            throw new InvalidOperationException(
                $"No Outbox processor is registered for '{claim.MessageType}'.");
        }

        return processor.ProcessAsync(claim, cancellationToken);
    }
}
