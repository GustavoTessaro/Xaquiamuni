using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Pedidos;

internal static class IntegridadeReservasPedido
{
    internal static bool Correspondem(Pedido pedido, IReadOnlyList<ReservaProdutoAcabado> reservas,
        EstadoReservaProdutoAcabado estadoEsperado)
    {
        var quantidades = reservas.GroupBy(reserva => reserva.ProdutoVarianteId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(reserva => (long)reserva.Quantidade));
        return pedido.Itens.Count > 0 && quantidades.Count == pedido.Itens.Count
            && pedido.Itens.All(item => quantidades.GetValueOrDefault(item.ProdutoVarianteId) == item.Quantidade)
            && reservas.All(reserva => reserva.PedidoId == pedido.Id && reserva.Estado == estadoEsperado
                && reserva.CriadaEmUtc == pedido.CriadoEmUtc && reserva.ExpiraEmUtc == pedido.PrazoPagamentoEmUtc
                && reserva.ConfirmadaEmUtc == pedido.PagamentoConfirmadoEmUtc);
    }
}
