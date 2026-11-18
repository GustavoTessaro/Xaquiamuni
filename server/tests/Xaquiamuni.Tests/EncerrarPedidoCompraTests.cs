using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class EncerrarPedidoCompraTests
{
    private static readonly DateTimeOffset Criado = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, EncerrarPedidoStatus.PrazoAindaVigente, EncerrarPedidoStatus.Cancelado)]
    [InlineData(0, EncerrarPedidoStatus.Expirado, EncerrarPedidoStatus.PrazoEncerrado)]
    [InlineData(1, EncerrarPedidoStatus.Expirado, EncerrarPedidoStatus.PrazoEncerrado)]
    public void PrazoComumDefineQualTransicaoPodeOcorrer(
        int ticks, EncerrarPedidoStatus expiracao, EncerrarPedidoStatus cancelamento)
    {
        var (pedidoExpiracao, reservaExpiracao) = Criar();
        var (pedidoCancelamento, reservaCancelamento) = Criar();
        var agora = pedidoExpiracao.PrazoPagamentoEmUtc.AddTicks(ticks);

        Assert.Equal(expiracao, EncerrarPedidoCompra.PrepararReservas(pedidoExpiracao, [reservaExpiracao], agora, null));
        Assert.Equal(expiracao == EncerrarPedidoStatus.Expirado
            ? EstadoReservaProdutoAcabado.Expirada : EstadoReservaProdutoAcabado.Ativa, reservaExpiracao.Estado);
        Assert.Equal(cancelamento, EncerrarPedidoCompra.PrepararReservas(
            pedidoCancelamento, [reservaCancelamento], agora, "teste"));
        Assert.Equal(cancelamento == EncerrarPedidoStatus.Cancelado
            ? EstadoReservaProdutoAcabado.Liberada : EstadoReservaProdutoAcabado.Ativa, reservaCancelamento.Estado);
    }

    [Fact]
    public void PreservaReservasConfirmadasEMisturasInconsistentes()
    {
        var (pedido, reserva) = Criar();
        var outroLote = ReservaProdutoAcabado.Criar(pedido.Id, reserva.ProdutoVarianteId, Guid.NewGuid(), 1, Criado);
        reserva.ConfirmarCompromisso(Criado.AddHours(1));
        Assert.Equal(EncerrarPedidoStatus.ReservasInconsistentes,
            EncerrarPedidoCompra.PrepararReservas(pedido, [reserva], Criado.AddDays(2), null));
        Assert.Equal(EncerrarPedidoStatus.ReservasInconsistentes,
            EncerrarPedidoCompra.PrepararReservas(pedido, [reserva, outroLote], Criado.AddHours(2), "teste"));
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, outroLote.Estado);
        pedido.ConfirmarPagamento(Guid.NewGuid(), Criado.AddHours(1));
        Assert.Equal(EncerrarPedidoStatus.EstadoIncompativel,
            EncerrarPedidoCompra.PrepararReservas(pedido, [reserva], Criado.AddDays(2), null));
        Assert.Equal(EncerrarPedidoStatus.EstadoIncompativel,
            EncerrarPedidoCompra.PrepararReservas(pedido, [reserva], Criado.AddHours(2), "teste"));
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, reserva.Estado);
    }

    [Fact]
    public void RepeticaoExigeQuePedidoEReservasTenhamOMesmoEncerramento()
    {
        var (pedido, reserva) = Criar();
        var agora = pedido.PrazoPagamentoEmUtc;
        pedido.Expirar(agora);
        Assert.Equal(EncerrarPedidoStatus.ReservasInconsistentes,
            EncerrarPedidoCompra.PrepararReservas(pedido, [reserva], agora, null));
        reserva.Expirar(agora);
        Assert.Equal(EncerrarPedidoStatus.Repetido,
            EncerrarPedidoCompra.PrepararReservas(pedido, [reserva], agora, null));
        Assert.Equal(EncerrarPedidoStatus.EstadoIncompativel,
            EncerrarPedidoCompra.PrepararReservas(pedido, [reserva], agora, "teste"));
    }

    private static (Pedido, ReservaProdutoAcabado) Criar()
    {
        var item = new DadosItemPedido(Guid.NewGuid(), Guid.NewGuid(), "Sabonete", "Lavanda", 1, 1000);
        var pedido = Pedido.Criar(Guid.NewGuid(), Criado, [item]);
        return (pedido, ReservaProdutoAcabado.Criar(pedido.Id, item.ProdutoVarianteId, Guid.NewGuid(), 1, Criado));
    }
}
