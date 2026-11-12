using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Outbox;

namespace Xaquiamuni.Infraestrutura.Outbox;

public sealed class EmailOutboxBackgroundService : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<EmailOutboxBackgroundServiceOptions> _options;
    private readonly ILogger<EmailOutboxBackgroundService> _logger;
    private int _nextMessageTypeIndex;

    public EmailOutboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<EmailOutboxBackgroundServiceOptions> options,
        ILogger<EmailOutboxBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessCycleAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                _logger.LogError("Email outbox polling cycle failed.");
            }

            try
            {
                await Task.Delay(options.CycleInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ProcessCycleAsync(
        EmailOutboxBackgroundServiceOptions options,
        CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>();
        var messageTypes = dispatcher.SupportedMessageTypes;
        if (messageTypes.Count == 0)
        {
            return;
        }

        var nextMessageTypeIndex = _nextMessageTypeIndex % messageTypes.Count;
        var claimedMessageIds = new HashSet<Guid>();
        for (var processedCount = 0; processedCount < options.BatchSize;)
        {
            EmailOutboxClaim? claim = null;
            var claimedMessageTypeIndex = -1;
            for (var offset = 0; offset < messageTypes.Count; offset++)
            {
                var messageTypeIndex = (nextMessageTypeIndex + offset) % messageTypes.Count;
                var messageType = messageTypes[messageTypeIndex];
                var claims = await outboxStore
                    .ClaimDueAsync(
                        messageType,
                        1,
                        LeaseDuration,
                        stoppingToken,
                        claimedMessageIds)
                    .ConfigureAwait(false);
                if (claims.Count == 0)
                {
                    continue;
                }

                if (claims.Count != 1)
                {
                    _logger.LogError("Email outbox store returned multiple messages for a single claim.");
                    return;
                }

                claim = claims[0];
                if (claim.MessageType != messageType)
                {
                    _logger.LogError("Email outbox store returned a message with an unexpected type.");
                    return;
                }

                if (!claimedMessageIds.Add(claim.Id))
                {
                    _logger.LogError("Email outbox store returned a message already claimed in this cycle.");
                    return;
                }

                claimedMessageTypeIndex = messageTypeIndex;
                break;
            }

            if (claim is null)
            {
                return;
            }

            nextMessageTypeIndex = (claimedMessageTypeIndex + 1) % messageTypes.Count;
            _nextMessageTypeIndex = nextMessageTypeIndex;
            try
            {
                await dispatcher.ProcessAsync(claim, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                _logger.LogError("Email outbox message processing failed.");
            }

            processedCount++;
        }
    }
}
