using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class PrepararPedidoCompraTests
{
    [Theory]
    [InlineData(SituacaoPedido.AguardandoPagamento)]
    [InlineData(SituacaoPedido.Cancelado)]
    [InlineData(SituacaoPedido.Expirado)]
    [InlineData(SituacaoPedido.Entregue)]
    public void RecusaEstadosIncompativeis(SituacaoPedido estado)
    {
        var inicio = DateTimeOffset.UnixEpoch;
        var admin = Guid.NewGuid();
        var item = new DadosItemPedido(Guid.NewGuid(), Guid.NewGuid(), "Sabonete", "Lavanda", 1, 1200);
        var pedido = Pedido.Criar(Guid.NewGuid(), inicio, [item]);
        switch (estado)
        {
            case SituacaoPedido.Cancelado: pedido.Cancelar(admin, inicio, "teste"); break;
            case SituacaoPedido.Expirado: pedido.Expirar(inicio.AddHours(24)); break;
            case SituacaoPedido.Entregue:
                pedido.ConfirmarPagamento(admin, inicio);
                pedido.IniciarPreparacao(admin, inicio);
                pedido.AutorizarRetirada(admin, inicio);
                pedido.ConfirmarEntrega(admin, inicio);
                break;
        }

        foreach (var etapa in Enum.GetValues<EtapaPreparacaoPedido>())
        {
            Assert.Equal(PrepararPedidoStatus.EstadoIncompativel,
                PrepararPedidoCompra.Aplicar(pedido, [], admin, inicio.AddDays(2), etapa));
        }
        Assert.Equal(estado, pedido.Situacao);
    }

    [Fact]
    public void RecusaReservaAusenteComQuantidadeIncorretaOuNaoConfirmada()
    {
        var inicio = DateTimeOffset.UnixEpoch;
        var admin = Guid.NewGuid();
        var item = new DadosItemPedido(Guid.NewGuid(), Guid.NewGuid(), "Sabonete", "Lavanda", 2, 1200);
        var pedido = Pedido.Criar(Guid.NewGuid(), inicio, [item]);
        pedido.ConfirmarPagamento(admin, inicio);
        var incompleta = ReservaProdutoAcabado.Criar(pedido.Id, item.ProdutoVarianteId, Guid.NewGuid(), 1, inicio);
        incompleta.ConfirmarCompromisso(inicio);
        var ativa = ReservaProdutoAcabado.Criar(pedido.Id, item.ProdutoVarianteId, Guid.NewGuid(), 2, inicio);
        foreach (ReservaProdutoAcabado[] reservas in new ReservaProdutoAcabado[][] { [], [incompleta], [ativa] })
        {
            Assert.Equal(PrepararPedidoStatus.ReservasInconsistentes, PrepararPedidoCompra.Aplicar(
                pedido, reservas, admin, inicio.AddHours(1), EtapaPreparacaoPedido.IniciarPreparacao));
        }
        Assert.Equal(SituacaoPedido.PagamentoConfirmado, pedido.Situacao);
        Assert.Null(pedido.PreparacaoIniciadaEmUtc);
    }
}
