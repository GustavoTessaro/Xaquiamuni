using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Pedidos;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ExpiracaoWorkerDuasInstanciasDisputamOMesmoPedidoSemBaixa()
    {
        var disputa = new RendezvousLockPedido();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: disputa);
        await using var context = database.CreateVerificationContext();
        var (vigente, variante) = await CriarPedidoParaPagamentoAsync(context);
        var lote = await context.LotesProdutoAcabado.AsNoTracking().SingleAsync();
        var agora = await LerUtcSqlAsync(context);
        var vencido = CriarPedidoVencido(context, vigente, variante, lote.Id, agora.AddDays(-2));
        await context.SaveChangesAsync();
        disputa.PedidoId = vencido.Id;

        var options = Options.Create(new ExpiracaoPedidosWorkerOptions { Enabled = true });
        var scopeFactory = database.Services.GetRequiredService<IServiceScopeFactory>();
        var logA = new RegistroCiclosExpiracao();
        var logB = new RegistroCiclosExpiracao();
        using var workerA = new ExpiracaoPedidosBackgroundService(scopeFactory, options, TimeProvider.System, logA);
        using var workerB = new ExpiracaoPedidosBackgroundService(scopeFactory, options, TimeProvider.System, logB);

        await Task.WhenAll(workerA.StartAsync(CancellationToken.None), workerB.StartAsync(CancellationToken.None));
        try
        {
            var ciclos = await Task.WhenAll(logA.ProximoAsync(), logB.ProximoAsync());
            Assert.Equal(2, ciclos.Sum(ciclo => Assert.IsType<int>(ciclo["Processados"])));
            Assert.Equal(1, ciclos.Sum(ciclo => Assert.IsType<int>(ciclo["Expirados"])));
            Assert.All(ciclos, ciclo => Assert.Equal(0, ciclo["Falhas"]));
        }
        finally
        {
            await Task.WhenAll(workerA.StopAsync(CancellationToken.None), workerB.StopAsync(CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(15));
        }

        context.ChangeTracker.Clear();
        var registros = await context.Pedidos.AsNoTracking().ToDictionaryAsync(pedido => pedido.Id);
        Assert.Equal(SituacaoPedido.AguardandoPagamento, registros[vigente.Id].Situacao);
        Assert.Equal(SituacaoPedido.Expirado, registros[vencido.Id].Situacao);
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().ToDictionaryAsync(reserva => reserva.PedidoId);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reservas[vigente.Id].Estado);
        Assert.Equal(EstadoReservaProdutoAcabado.Expirada, reservas[vencido.Id].Estado);
        Assert.Equal(registros[vencido.Id].EncerradoEmUtc, reservas[vencido.Id].EncerradaEmUtc);
        Assert.Equal(1, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExpiracaoWorkerLimiteDaVarreduraAdiaNovosVencimentosAteReinicio()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var context = database.CreateVerificationContext();
        var (vigente, variante) = await CriarPedidoParaPagamentoAsync(context);
        var lote = await context.LotesProdutoAcabado.AsNoTracking().SingleAsync();
        var agora = await LerUtcSqlAsync(context);
        var primeiro = CriarPedidoVencido(context, vigente, variante, lote.Id, agora.AddDays(-3));
        var segundo = CriarPedidoVencido(context, vigente, variante, lote.Id, agora.AddDays(-2));
        await context.SaveChangesAsync();

        var encerrar = database.Services.GetRequiredService<EncerrarPedidoCompra>();
        relogio.Agora = agora;
        var loteInicial = await encerrar.ProcessarLoteAsync(1);
        Assert.Equal(new ResultadoEncerramentoPedido(primeiro.Id, EncerrarPedidoStatus.Expirado),
            Assert.Single(loteInicial.Resultados));
        Assert.Equal(agora, loteInicial.Proximo?.LimiteVarreduraUtc);

        relogio.Agora = vigente.PrazoPagamentoEmUtc;
        var loteFinal = await encerrar.ProcessarLoteAsync(1, loteInicial.Proximo);
        Assert.Equal(new ResultadoEncerramentoPedido(segundo.Id, EncerrarPedidoStatus.Expirado),
            Assert.Single(loteFinal.Resultados));
        Assert.Null(loteFinal.Proximo);

        var novaVarredura = await encerrar.ProcessarLoteAsync(1);
        Assert.Equal(new ResultadoEncerramentoPedido(vigente.Id, EncerrarPedidoStatus.Expirado),
            Assert.Single(novaVarredura.Resultados));
    }

    private sealed class RendezvousLockPedido : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _ambos = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _chegadas;
        public Guid PedidoId { get; set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (PedidoId != Guid.Empty && command.CommandText.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase)
                && command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, $"finished-order:{PedidoId:D}")))
            {
                if (Interlocked.Increment(ref _chegadas) == 2)
                {
                    _ambos.TrySetResult();
                }
                await _ambos.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }
}
