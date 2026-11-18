using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task EncerramentoExpiraNoLimiteComVariasAlocacoesERepeticao()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context, variosLotes: true);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var encerrar = database.Services.GetRequiredService<EncerrarPedidoCompra>();

        relogio.Agora = pedido.PrazoPagamentoEmUtc.AddTicks(-1);
        Assert.Equal(EncerrarPedidoStatus.PrazoAindaVigente, await encerrar.ExpirarAsync(pedido.Id));
        relogio.Agora = pedido.PrazoPagamentoEmUtc;
        Assert.Equal(EncerrarPedidoStatus.PrazoEncerrado, await encerrar.CancelarAsync(pedido.Id, admin, "teste"));
        Assert.Equal(EncerrarPedidoStatus.Expirado, await encerrar.ExpirarAsync(pedido.Id));
        relogio.Agora = pedido.PrazoPagamentoEmUtc.AddDays(2);
        Assert.Equal(EncerrarPedidoStatus.Repetido, await encerrar.ExpirarAsync(pedido.Id));

        var persistido = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(SituacaoPedido.Expirado, persistido.Situacao);
        Assert.Equal(pedido.PrazoPagamentoEmUtc, persistido.PrazoPagamentoEmUtc);
        Assert.Equal(pedido.PrazoPagamentoEmUtc, persistido.EncerradoEmUtc);
        Assert.Null(persistido.EncerradoPorId);
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        Assert.Equal(3, reservas.Count);
        Assert.All(reservas, reserva =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Expirada, reserva.Estado);
            Assert.Equal(persistido.EncerradoEmUtc, reserva.EncerradaEmUtc);
        });
        Assert.Equal(3, (await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync()).Sum(s => s.QuantidadeFisica));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task EncerramentoCancelaSemBaixaEAuditaMotivoEAtorOriginais()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var outroAdmin = await CriarAdministradorPagamentoAsync(context);
        var bloqueado = await CriarAdministradorPagamentoAsync(context, bloqueado: true);
        var encerrar = Encerramento(context);
        Assert.Equal(EncerrarPedidoStatus.DadosInvalidos, await encerrar.CancelarAsync(pedido.Id, admin, " "));
        Assert.Equal(EncerrarPedidoStatus.DadosInvalidos,
            await encerrar.CancelarAsync(pedido.Id, admin, new string('a', Pedido.MotivoCancelamentoMaxLength + 1)));
        Assert.Equal(EncerrarPedidoStatus.AdministradorNaoAutorizado,
            await encerrar.CancelarAsync(pedido.Id, pedido.ClienteId, "teste"));
        Assert.Equal(EncerrarPedidoStatus.AdministradorNaoAutorizado,
            await encerrar.CancelarAsync(pedido.Id, bloqueado, "teste"));
        Assert.Equal(EncerrarPedidoStatus.PedidoNaoEncontrado, await encerrar.CancelarAsync(Guid.NewGuid(), admin, "teste"));

        var antes = await LerUtcSqlAsync(context);
        Assert.Equal(EncerrarPedidoStatus.Cancelado, await encerrar.CancelarAsync(pedido.Id, admin, "  motivo teste  "));
        var depois = await LerUtcSqlAsync(context);
        Assert.Equal(EncerrarPedidoStatus.Repetido, await encerrar.CancelarAsync(pedido.Id, outroAdmin, "motivo teste"));
        Assert.Equal(EncerrarPedidoStatus.MotivoDivergente, await encerrar.CancelarAsync(pedido.Id, admin, "divergente"));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.EstadoIncompativel, await ConfirmarPagamento(context).ExecuteAsync(pedido.Id, admin));

        var persistido = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(SituacaoPedido.Cancelado, persistido.Situacao);
        Assert.Equal(admin, persistido.EncerradoPorId);
        Assert.Equal("motivo teste", persistido.MotivoCancelamento);
        var instante = Assert.IsType<DateTimeOffset>(persistido.EncerradoEmUtc);
        Assert.Equal(TimeSpan.Zero, instante.Offset);
        Assert.InRange(instante, antes, depois);
        var reserva = await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoReservaProdutoAcabado.Liberada, reserva.Estado);
        Assert.Equal(instante, reserva.EncerradaEmUtc);
        Assert.Equal("motivo teste", reserva.MotivoEncerramento);
        Assert.Equal(1, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task EncerramentoLoteAvancaComCursorApesarDeRollbackIsolado()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (vigente, variante) = await CriarPedidoParaPagamentoAsync(context);
        var loteId = (await context.LotesProdutoAcabado.AsNoTracking().SingleAsync()).Id;
        var agora = await LerUtcSqlAsync(context);
        var primeiro = CriarPedidoVencido(context, vigente, variante, loteId, agora.AddDays(-3));
        var segundo = CriarPedidoVencido(context, vigente, variante, loteId, agora.AddDays(-2));
        var terceiro = CriarPedidoVencido(context, vigente, variante, loteId, agora.AddDays(-2));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(context.Database.GetConnectionString())
            .AddInterceptors(new FalharAoGravarEncerramento(primeiro.Id))
            .Options;
        await using var comFalha = new XaquiamuniDbContext(options);
        var encerrar = Encerramento(comFalha);
        var lote = await encerrar.ProcessarLoteAsync(limite: 2);
        Assert.Equal(2, lote.Resultados.Count);
        Assert.Equal(new ResultadoEncerramentoPedido(primeiro.Id, EncerrarPedidoStatus.FalhaPersistencia), lote.Resultados[0]);
        Assert.Equal(EncerrarPedidoStatus.Expirado, lote.Resultados[1].Status);
        Assert.NotNull(lote.Proximo);

        var restante = await encerrar.ProcessarLoteAsync(limite: 2, apos: lote.Proximo);
        var ultimo = Assert.Single(restante.Resultados);
        Assert.Equal(EncerrarPedidoStatus.Expirado, ultimo.Status);
        Assert.Equal(new[] { segundo.Id, terceiro.Id }.Order(), new[] { lote.Resultados[1].PedidoId, ultimo.PedidoId }.Order());
        Assert.Null(restante.Proximo);
        Assert.Equal(SituacaoPedido.AguardandoPagamento,
            await context.Pedidos.Where(p => p.Id == primeiro.Id).Select(p => p.Situacao).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, await context.ReservasProdutoAcabado
            .Where(r => r.PedidoId == primeiro.Id).Select(r => r.Estado).SingleAsync());
        Assert.Equal(SituacaoPedido.AguardandoPagamento,
            await context.Pedidos.Where(p => p.Id == vigente.Id).Select(p => p.Situacao).SingleAsync());
        Assert.Equal(new ResultadoEncerramentoPedido(primeiro.Id, EncerrarPedidoStatus.Expirado),
            Assert.Single((await Encerramento(context).ProcessarLoteAsync()).Resultados));
        Assert.Equal(1, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task EncerramentoConcorrenteComPagamentoRespeitaPrazoEEstadoPago()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var context = database.CreateVerificationContext();
        var (pedido, variante) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        await using var escopoA = database.Services.CreateAsyncScope();
        await using var escopoB = database.Services.CreateAsyncScope();
        var encerrar = escopoA.ServiceProvider.GetRequiredService<EncerrarPedidoCompra>();
        var confirmar = escopoB.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedido>();

        relogio.Agora = pedido.PrazoPagamentoEmUtc;
        var (expirar, pagamento) = await DisputarAsync(
            () => encerrar.ExpirarAsync(pedido.Id), () => confirmar.ExecuteAsync(pedido.Id, admin));
        Assert.Equal(EncerrarPedidoStatus.Expirado, expirar);
        Assert.Contains(pagamento, new[] { ConfirmarPagamentoPedidoStatus.PrazoEncerrado, ConfirmarPagamentoPedidoStatus.EstadoIncompativel });

        var segundo = await Compra(context).ExecuteAsync(new(pedido.ClienteId, Guid.NewGuid(),
            [new(variante.ProdutoId, variante.Id, 1)]));
        Assert.Equal(CriarPedidoCompraStatus.Sucesso, segundo.Status);
        var novoPedido = Assert.IsType<Pedido>(segundo.Pedido);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await ConfirmarPagamento(context).ExecuteAsync(novoPedido.Id, admin));
        relogio.Agora = novoPedido.PrazoPagamentoEmUtc;
        Assert.Equal(EncerrarPedidoStatus.EstadoIncompativel, await encerrar.ExpirarAsync(novoPedido.Id));
        Assert.Equal(EncerrarPedidoStatus.EstadoIncompativel, await encerrar.CancelarAsync(novoPedido.Id, admin, "teste"));
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, await context.ReservasProdutoAcabado
            .Where(r => r.PedidoId == novoPedido.Id).Select(r => r.Estado).SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task EncerramentoSerializaCancelamentoComConfirmacaoERestauraSeGravacaoFalhar()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(context.Database.GetConnectionString())
            .AddInterceptors(new FalharAoGravarEncerramento(pedido.Id))
            .Options;
        await using var comFalha = new XaquiamuniDbContext(options);
        await Assert.ThrowsAsync<DbUpdateException>(() => Encerramento(comFalha).CancelarAsync(pedido.Id, admin, "teste"));
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa,
            (await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(SituacaoPedido.AguardandoPagamento, (await context.Pedidos.AsNoTracking().SingleAsync()).Situacao);

        await using var outraConexao = database.CreateVerificationContext();
        var (cancelar, pagar) = await DisputarAsync(
            () => Encerramento(context).CancelarAsync(pedido.Id, admin, "teste"),
            () => ConfirmarPagamento(outraConexao).ExecuteAsync(pedido.Id, admin));
        var cancelado = cancelar == EncerrarPedidoStatus.Cancelado;
        Assert.Equal(cancelado ? ConfirmarPagamentoPedidoStatus.EstadoIncompativel : ConfirmarPagamentoPedidoStatus.Confirmado, pagar);
        Assert.Equal(cancelado ? EncerrarPedidoStatus.Cancelado : EncerrarPedidoStatus.EstadoIncompativel, cancelar);
        Assert.Equal(cancelado ? SituacaoPedido.Cancelado : SituacaoPedido.PagamentoConfirmado,
            (await context.Pedidos.AsNoTracking().SingleAsync()).Situacao);
        Assert.Equal(cancelado ? EstadoReservaProdutoAcabado.Liberada : EstadoReservaProdutoAcabado.Confirmada,
            (await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).Estado);
    }

    private static EncerrarPedidoCompra Encerramento(XaquiamuniDbContext context) => new(Repositorio(context));

    private static Pedido CriarPedidoVencido(
        XaquiamuniDbContext context, Pedido modelo, ProdutoVariante variante, Guid lote, DateTimeOffset criacao)
    {
        var item = new DadosItemPedido(variante.ProdutoId, variante.Id, "Sabonete", "Padrao", 1, 1000);
        var pedido = Pedido.Criar(modelo.ClienteId, criacao, [item]);
        context.Pedidos.Add(pedido);
        context.ReservasProdutoAcabado.Add(ReservaProdutoAcabado.Criar(pedido.Id, variante.Id, lote, 1, criacao));
        return pedido;
    }

    private sealed class FalharAoGravarEncerramento(Guid pedidoId) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<Pedido>().Any(entry => entry.Entity.Id == pedidoId
                    && entry.State == EntityState.Modified))
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                var reserva = Assert.Single(context.ChangeTracker.Entries<ReservaProdutoAcabado>());
                Assert.Equal(EntityState.Unchanged, reserva.State);
                Assert.Contains(reserva.Entity.Estado, new[] { EstadoReservaProdutoAcabado.Liberada, EstadoReservaProdutoAcabado.Expirada });
                throw new DbUpdateException("Falha injetada depois de gravar a reserva.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
