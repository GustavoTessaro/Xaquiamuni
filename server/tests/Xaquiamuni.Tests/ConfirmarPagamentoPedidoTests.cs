using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class ConfirmarPagamentoPedidoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, ConfirmarPagamentoPedidoStatus.Confirmado)]
    [InlineData(0, ConfirmarPagamentoPedidoStatus.PrazoEncerrado)]
    [InlineData(1, ConfirmarPagamentoPedidoStatus.PrazoEncerrado)]
    public void PrazoFechaNoInstanteExato(int ticks, ConfirmarPagamentoPedidoStatus esperado)
    {
        var (pedido, reserva) = Criar();
        Assert.Equal(esperado, ConfirmarPagamentoPedido.PrepararReservas(
            pedido, [reserva], pedido.PrazoPagamentoEmUtc.AddTicks(ticks)));
        Assert.Equal(esperado == ConfirmarPagamentoPedidoStatus.Confirmado
            ? EstadoReservaProdutoAcabado.Confirmada : EstadoReservaProdutoAcabado.Ativa, reserva.Estado);
    }

    [Theory]
    [InlineData(SituacaoPedido.PagamentoConfirmado, ConfirmarPagamentoPedidoStatus.Repetido)]
    [InlineData(SituacaoPedido.EmPreparacao, ConfirmarPagamentoPedidoStatus.EstadoIncompativel)]
    [InlineData(SituacaoPedido.ProntoParaRetirada, ConfirmarPagamentoPedidoStatus.EstadoIncompativel)]
    [InlineData(SituacaoPedido.Entregue, ConfirmarPagamentoPedidoStatus.EstadoIncompativel)]
    [InlineData(SituacaoPedido.Cancelado, ConfirmarPagamentoPedidoStatus.EstadoIncompativel)]
    [InlineData(SituacaoPedido.Expirado, ConfirmarPagamentoPedidoStatus.EstadoIncompativel)]
    public void RepeticaoSoAceitaPagamentoConfirmado(SituacaoPedido situacao, ConfirmarPagamentoPedidoStatus esperado)
    {
        var (pedido, reserva) = Criar();
        var ator = Guid.NewGuid();
        if (situacao == SituacaoPedido.Expirado)
        {
            pedido.Expirar(pedido.PrazoPagamentoEmUtc);
        }
        else if (situacao == SituacaoPedido.Cancelado)
        {
            pedido.Cancelar(ator, Inicio.AddHours(1), "teste");
        }
        else
        {
            reserva.ConfirmarCompromisso(Inicio.AddHours(1));
            pedido.ConfirmarPagamento(ator, Inicio.AddHours(1));
            if (situacao >= SituacaoPedido.EmPreparacao) pedido.IniciarPreparacao(ator, Inicio.AddHours(2));
            if (situacao >= SituacaoPedido.ProntoParaRetirada) pedido.AutorizarRetirada(ator, Inicio.AddHours(3));
            if (situacao == SituacaoPedido.Entregue) pedido.ConfirmarEntrega(ator, Inicio.AddHours(4));
        }

        Assert.Equal(esperado, ConfirmarPagamentoPedido.PrepararReservas(pedido, [reserva], Inicio.AddDays(2)));
        Assert.Equal(situacao, pedido.Situacao);
    }

    [Fact]
    public void RecusaQuantidadePrazoEVinculoInconsistentesSemMudarReservaValida()
    {
        var (pedido, valida) = Criar(quantidade: 2);
        var variante = valida.ProdutoVarianteId;
        var lote = Guid.NewGuid();
        var parcial = ReservaProdutoAcabado.Criar(pedido.Id, variante, lote, 1, Inicio);
        var outroPedido = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante, lote, 1, Inicio);
        var outroPrazo = ReservaProdutoAcabado.Criar(pedido.Id, variante, lote, 1, Inicio, Inicio.AddHours(23));
        var inicioDiferente = ReservaProdutoAcabado.Criar(pedido.Id, variante, lote, 1, Inicio.AddMinutes(1),
            pedido.PrazoPagamentoEmUtc);
        ReservaProdutoAcabado[][] invalidas = [[], [parcial], [parcial, outroPedido], [parcial, outroPrazo],
            [parcial, inicioDiferente]];
        foreach (var reservas in invalidas)
        {
            Assert.Equal(ConfirmarPagamentoPedidoStatus.ReservasInconsistentes,
                ConfirmarPagamentoPedido.PrepararReservas(pedido, reservas, Inicio.AddHours(1)));
        }
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, valida.Estado);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, parcial.Estado);
    }

    private static (Pedido, ReservaProdutoAcabado) Criar(int quantidade = 1)
    {
        var item = new DadosItemPedido(Guid.NewGuid(), Guid.NewGuid(), "Sabonete", "Lavanda", quantidade, 1200);
        var pedido = Pedido.Criar(Guid.NewGuid(), Inicio, [item]);
        return (pedido, ReservaProdutoAcabado.Criar(pedido.Id, item.ProdutoVarianteId, Guid.NewGuid(), quantidade, Inicio));
    }
}
