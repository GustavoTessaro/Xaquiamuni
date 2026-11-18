using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Infraestrutura.Pedidos;

public sealed class ExpiracaoPedidosBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExpiracaoPedidosWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiracaoPedidosBackgroundService> logger) : BackgroundService
{
    private CursorPedidosVencidos? _cursor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = options.Value;
        if (!configuracao.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarCicloAsync(configuracao, stoppingToken).ConfigureAwait(false);
                await Task.Delay(configuracao.CycleInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ProcessarCicloAsync(ExpiracaoPedidosWorkerOptions configuracao, CancellationToken stoppingToken)
    {
        var processados = 0;
        var expirados = 0;
        var falhas = 0;
        var falhaCiclo = false;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var encerrar = scope.ServiceProvider.GetRequiredService<EncerrarPedidoCompra>();
            for (var pagina = 0; pagina < configuracao.MaxPagesPerCycle; pagina++)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var lote = await encerrar.ProcessarLoteAsync(configuracao.BatchSize, _cursor, stoppingToken)
                    .ConfigureAwait(false);
                processados += lote.Resultados.Count;
                expirados += lote.Resultados.Count(resultado => resultado.Status == EncerrarPedidoStatus.Expirado);
                falhas += lote.Resultados.Count(resultado => resultado.Status is not
                    (EncerrarPedidoStatus.Expirado or EncerrarPedidoStatus.Repetido or EncerrarPedidoStatus.EstadoIncompativel));

                // O cursor conserva o limite UTC da varredura e volta ao início ao esgotá-la.
                _cursor = lote.Proximo;
                if (_cursor is null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            falhaCiclo = true;
            // Mensagens de exceção do provedor podem conter detalhes da conexão.
            logger.LogError("Falha no ciclo de vencimento de pedidos ({TipoFalha}). Nova tentativa no próximo ciclo.",
                exception.GetType().Name);
        }

        var nivel = falhas > 0 || falhaCiclo ? LogLevel.Warning : processados > 0 ? LogLevel.Information : LogLevel.Debug;
        logger.Log(nivel, "Vencimento de pedidos. Processados={Processados} Expirados={Expirados} Falhas={Falhas} FalhaCiclo={FalhaCiclo}.",
            processados, expirados, falhas, falhaCiclo);
    }
}
