using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task EntregaPedidoMultiLoteAuditaEMantemResultadoOriginalNaRepeticao()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, admin) = await CriarPedidoProntoAsync(context, variosLotes: true);
        var outroAdmin = await CriarAdministradorPagamentoAsync(context);
        var caso = database.Services.GetRequiredService<ConfirmarEntregaPedido>();
        var antes = await LerUtcSqlAsync(context);
        var resultado = await caso.ExecuteAsync(pedido.Id, admin, true);
        var depois = await LerUtcSqlAsync(context);
        Assert.Equal(ConfirmarEntregaPedidoStatus.Entregue, resultado.Status);
        var entrega = Assert.IsType<EntregaPedidoConfirmada>(resultado.Entrega);
        var repetida = await caso.ExecuteAsync(pedido.Id, outroAdmin, true);
        Assert.Equal(ConfirmarEntregaPedidoStatus.Repetida, repetida.Status);
        Assert.Equal(entrega.MovimentoIds, repetida.Entrega!.MovimentoIds);
        Assert.Equal(admin, repetida.Entrega.ResponsavelId);
        Assert.Equal(entrega.EntregueEmUtc, repetida.Entrega.EntregueEmUtc);
        Assert.InRange(entrega.EntregueEmUtc, antes, depois);
        Assert.Equal(TimeSpan.Zero, entrega.EntregueEmUtc.Offset);

        var persistido = await context.Pedidos.AsNoTracking().Include(p => p.Itens).SingleAsync();
        Assert.Equal(SituacaoPedido.Entregue, persistido.Situacao);
        Assert.Equal(entrega.EntregueEmUtc, persistido.EncerradoEmUtc);
        Assert.Equal(admin, persistido.EncerradoPorId);
        Assert.Equal(pedido.TotalCentavos, persistido.TotalCentavos);
        Assert.Equal(pedido.PrazoPagamentoEmUtc, persistido.PrazoPagamentoEmUtc);
        Assert.Equal(pedido.Itens.Select(i => i.Id).Order(), persistido.Itens.Select(i => i.Id).Order());
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        var movimentos = await context.MovimentosProdutoAcabado.AsNoTracking()
            .Where(m => m.ReservaProdutoAcabadoId != null).ToListAsync();
        Assert.Equal(3, movimentos.Count);
        Assert.True(ConfirmarEntregaPedido.ReservasCorrespondem(persistido, reservas));
        Assert.True(ConfirmarEntregaPedido.MovimentosCorrespondem(persistido, reservas, movimentos));
        Assert.False(ConfirmarEntregaPedido.MovimentosCorrespondem(persistido, reservas, movimentos.Skip(1).ToArray()));
        Assert.False(ConfirmarEntregaPedido.MovimentosCorrespondem(persistido, reservas, [movimentos[0], movimentos[0], movimentos[2]]));
        Assert.All(await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync(), saldo => Assert.Equal(0, saldo.QuantidadeFisica));
        Assert.Equal(6, await context.MovimentosProdutoAcabado.CountAsync());
        Assert.False((await Repositorio(context).EstornarAsync(
            new(movimentos[0].Id, admin, "teste", "estorno-entrega"))).Sucesso);
        Assert.Equal(ConsumirReservasStatus.EstadoIncompativel, (await Repositorio(context).ConsumirAsync(pedido.Id, admin)).Status);
    }

    [SqlServerIntegrationFact]
    public async Task EntregaPedidoDisputaUmLoteEResultaEmUmaUnicaBaixa()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, admin) = await CriarPedidoProntoAsync(context);
        var outroAdmin = await CriarAdministradorPagamentoAsync(context);
        await using var outra = database.CreateVerificationContext();
        var (a, b) = await DisputarAsync(() => Entrega(context).ExecuteAsync(pedido.Id, admin, true),
            () => Entrega(outra).ExecuteAsync(pedido.Id, outroAdmin, true));
        Assert.Equal(new[] { ConfirmarEntregaPedidoStatus.Entregue, ConfirmarEntregaPedidoStatus.Repetida },
            new[] { a.Status, b.Status }.Order());
        Assert.Equal(a.Entrega!.ResponsavelId, b.Entrega!.ResponsavelId);
        Assert.Equal(a.Entrega.MovimentoIds, b.Entrega.MovimentoIds);
        Assert.Equal(0, (await context.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
        Assert.Equal(2, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task EntregaPedidoRecusaContaEstadoEReservaAusente()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var bloqueado = await CriarAdministradorPagamentoAsync(context, bloqueado: true);
        var caso = Entrega(context);
        foreach (var ator in new[] { Guid.NewGuid(), pedido.ClienteId, bloqueado })
        {
            Assert.Equal(ConfirmarEntregaPedidoStatus.AdministradorNaoAutorizado, (await caso.ExecuteAsync(pedido.Id, ator, true)).Status);
        }
        Assert.Equal(ConfirmarEntregaPedidoStatus.PedidoNaoEncontrado, (await caso.ExecuteAsync(Guid.NewGuid(), admin, true)).Status);
        Assert.Equal(ConfirmarEntregaPedidoStatus.EstadoIncompativel, (await caso.ExecuteAsync(pedido.Id, admin, true)).Status);
        await DeixarPedidoProntoAsync(context, pedido.Id, admin);
        context.ReservasProdutoAcabado.Remove(await context.ReservasProdutoAcabado.SingleAsync());
        await context.SaveChangesAsync();
        Assert.Equal(ConfirmarEntregaPedidoStatus.ReservasInconsistentes, (await caso.ExecuteAsync(pedido.Id, admin, true)).Status);
        Assert.Equal(SituacaoPedido.ProntoParaRetirada, (await context.Pedidos.AsNoTracking().SingleAsync()).Situacao);
        Assert.Equal(1, (await context.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
    }

    [SqlServerIntegrationFact]
    public async Task EntregaPedidoBloqueiaLoteVencidoVarianteInativaESaldoInsuficiente()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var context = database.CreateVerificationContext();
        var (pedido, admin) = await CriarPedidoProntoAsync(context, variosLotes: true);
        var caso = database.Services.GetRequiredService<ConfirmarEntregaPedido>();
        relogio.Agora = pedido.PrazoPagamentoEmUtc.AddDays(20);
        Assert.Equal(ConfirmarEntregaPedidoStatus.LoteInelegivel, (await caso.ExecuteAsync(pedido.Id, admin, true)).Status);
        relogio.Agora = null;

        var produto = await context.Produtos.Include(p => p.Variantes).SingleAsync();
        var alternativa = produto.Variantes.Single(v => !v.Padrao);
        produto.DesativarVariante(alternativa.Id);
        await context.SaveChangesAsync();
        Assert.Equal(ConfirmarEntregaPedidoStatus.LoteInelegivel, (await caso.ExecuteAsync(pedido.Id, admin, true)).Status);
        produto.AtivarVariante(alternativa.Id);
        await context.SaveChangesAsync();
        var lote = await context.LotesProdutoAcabado.Where(l => l.ProdutoVarianteId == alternativa.Id).FirstAsync();
        // Simula uma divergência de estoque somente na base descartável.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [SaldosProdutoAcabado] SET [QuantidadeVendavel] = 0 WHERE [LoteId] = {lote.Id}");
        Assert.Equal(ConfirmarEntregaPedidoStatus.SaldoInsuficiente, (await caso.ExecuteAsync(pedido.Id, admin, true)).Status);

        Assert.Equal(SituacaoPedido.ProntoParaRetirada, await context.Pedidos.Select(p => p.Situacao).SingleAsync());
        Assert.All(await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync(),
            r => Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task EntregaPedidoReverteNaPrimeiraBaixaEEntreMovimentosEPedido()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, admin) = await CriarPedidoProntoAsync(context, variosLotes: true);
        var falhaPrimeira = new FalharDepoisDaPrimeiraBaixa();
        var falhaMovimentos = new FalharAntesDeAtualizarPedidoEntregue();
        foreach (var interceptor in new IInterceptor[] { falhaPrimeira, falhaMovimentos })
        {
            var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(context.Database.GetConnectionString())
                .AddInterceptors(interceptor).Options;
            await using var comFalha = new XaquiamuniDbContext(options);
            await Assert.ThrowsAnyAsync<Exception>(() => Entrega(comFalha).ExecuteAsync(pedido.Id, admin, true));
            Assert.Equal(SituacaoPedido.ProntoParaRetirada, await context.Pedidos.Select(p => p.Situacao).SingleAsync());
            Assert.All(await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync(),
                r => Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado));
            Assert.All(await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync(), saldo => Assert.Equal(1, saldo.QuantidadeFisica));
            Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
        }
        Assert.Equal(1, falhaPrimeira.BaixasExecutadas);
        Assert.True(falhaMovimentos.Executada);
        Assert.Equal(ConfirmarEntregaPedidoStatus.Entregue, (await Entrega(context).ExecuteAsync(pedido.Id, admin, true)).Status);
    }

    private static ConfirmarEntregaPedido Entrega(XaquiamuniDbContext context) => new(Repositorio(context));

    private static async Task<(Pedido Pedido, Guid Admin)> CriarPedidoProntoAsync(XaquiamuniDbContext context, bool variosLotes = false)
    {
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context, variosLotes);
        var admin = await CriarAdministradorPagamentoAsync(context);
        await DeixarPedidoProntoAsync(context, pedido.Id, admin);
        return (pedido, admin);
    }

    private static async Task DeixarPedidoProntoAsync(XaquiamuniDbContext context, Guid pedidoId, Guid admin)
    {
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await ConfirmarPagamento(context).ExecuteAsync(pedidoId, admin));
        var preparar = new PrepararPedidoCompra(Repositorio(context));
        Assert.Equal(PrepararPedidoStatus.Sucesso, await preparar.IniciarPreparacaoAsync(pedidoId, admin));
        Assert.Equal(PrepararPedidoStatus.Sucesso, await preparar.LiberarParaRetiradaAsync(pedidoId, admin));
    }

    private sealed class FalharAntesDeAtualizarPedidoEntregue : SaveChangesInterceptor
    {
        public bool Executada { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<Pedido>().Any(entry =>
                entry.State == EntityState.Modified && entry.Entity.Situacao == SituacaoPedido.Entregue))
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                var movimentos = context.ChangeTracker.Entries<MovimentoProdutoAcabado>().ToList();
                Assert.Equal(3, movimentos.Count);
                Assert.All(movimentos, m => Assert.Equal(EntityState.Unchanged, m.State));
                Executada = true;
                throw new FalhaInjetada();
            }

            return ValueTask.FromResult(result);
        }
    }
}
