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
        var processor = scope.ServiceProvider.GetRequiredService<EmailConfirmationOutboxProcessor>();
        var claimedMessageIds = new HashSet<Guid>();
        for (var processedCount = 0; processedCount < options.BatchSize; processedCount++)
        {
            var claims = await outboxStore
                .ClaimDueAsync(
                    EmailOutboxMessageType.EmailConfirmation,
                    1,
                    LeaseDuration,
                    stoppingToken,
                    claimedMessageIds)
                .ConfigureAwait(false);
            if (claims.Count == 0)
            {
                return;
            }

            var claim = claims[0];
            if (!claimedMessageIds.Add(claim.Id))
            {
                _logger.LogError("Email outbox store returned a message already claimed in this cycle.");
                return;
            }

            try
            {
                await processor.ProcessAsync(claim, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                _logger.LogError("Email outbox message processing failed.");
            }
        }
    }
}
