using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Pedidos;

public enum EncerrarPedidoStatus
{
    Expirado,
    Cancelado,
    Repetido,
    DadosInvalidos,
    AdministradorNaoAutorizado,
    PedidoNaoEncontrado,
    EstadoIncompativel,
    PrazoAindaVigente,
    PrazoEncerrado,
    MotivoDivergente,
    ReservasInconsistentes,
    ConflitoConcorrencia,
    FalhaPersistencia,
}

public sealed record CursorPedidosVencidos(
    DateTimeOffset PrazoPagamentoEmUtc, Guid PedidoId, DateTimeOffset? LimiteVarreduraUtc = null);
public sealed record ResultadoEncerramentoPedido(Guid PedidoId, EncerrarPedidoStatus Status);

public sealed record LotePedidosVencidos(
    IReadOnlyList<ResultadoEncerramentoPedido> Resultados, CursorPedidosVencidos? Proximo);

public interface IEncerrarPedidoCompraRepository
{
    Task<EncerrarPedidoStatus> ExpirarAsync(Guid pedidoId, CancellationToken cancellationToken);
    Task<EncerrarPedidoStatus> CancelarAsync(Guid pedidoId, Guid administradorId, string motivo, CancellationToken cancellationToken);
    Task<LotePedidosVencidos> ProcessarLoteAsync(int limite, CursorPedidosVencidos? apos, CancellationToken cancellationToken);
}

public sealed class EncerrarPedidoCompra(IEncerrarPedidoCompraRepository repository)
{
    public Task<EncerrarPedidoStatus> ExpirarAsync(Guid pedidoId, CancellationToken cancellationToken = default) =>
        pedidoId == Guid.Empty
            ? Task.FromResult(EncerrarPedidoStatus.DadosInvalidos)
            : repository.ExpirarAsync(pedidoId, cancellationToken);

    public Task<EncerrarPedidoStatus> CancelarAsync(
        Guid pedidoId, Guid administradorId, string? motivo, CancellationToken cancellationToken = default)
    {
        var normalizado = motivo?.Trim();
        return pedidoId == Guid.Empty || administradorId == Guid.Empty
            || string.IsNullOrEmpty(normalizado) || normalizado.Length > Pedido.MotivoCancelamentoMaxLength
            ? Task.FromResult(EncerrarPedidoStatus.DadosInvalidos)
            : repository.CancelarAsync(pedidoId, administradorId, normalizado, cancellationToken);
    }

    public Task<LotePedidosVencidos> ProcessarLoteAsync(
        int limite = 50, CursorPedidosVencidos? apos = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limite, 100);
        return repository.ProcessarLoteAsync(limite, apos, cancellationToken);
    }

    // Chamado sob os locks do pedido e das variantes. Motivo nulo identifica expiração.
    public static EncerrarPedidoStatus PrepararReservas(
        Pedido pedido, IReadOnlyList<ReservaProdutoAcabado> reservas, DateTimeOffset agoraUtc, string? motivo)
    {
        var expiracao = motivo is null;
        var destinoPedido = expiracao ? SituacaoPedido.Expirado : SituacaoPedido.Cancelado;
        var destinoReserva = expiracao ? EstadoReservaProdutoAcabado.Expirada : EstadoReservaProdutoAcabado.Liberada;
        var repeticao = pedido.Situacao == destinoPedido;
        if ((!repeticao && pedido.Situacao != SituacaoPedido.AguardandoPagamento)
            || pedido.PagamentoConfirmadoEmUtc is not null)
        {
            return EncerrarPedidoStatus.EstadoIncompativel;
        }

        if (!repeticao && expiracao && agoraUtc < pedido.PrazoPagamentoEmUtc)
        {
            return EncerrarPedidoStatus.PrazoAindaVigente;
        }
        if (!repeticao && !expiracao && agoraUtc >= pedido.PrazoPagamentoEmUtc)
        {
            return EncerrarPedidoStatus.PrazoEncerrado;
        }
        if (repeticao && !string.Equals(motivo, pedido.MotivoCancelamento, StringComparison.Ordinal))
        {
            return EncerrarPedidoStatus.MotivoDivergente;
        }

        if (!IntegridadeReservasPedido.Correspondem(pedido, reservas,
                repeticao ? destinoReserva : EstadoReservaProdutoAcabado.Ativa)
            || reservas.Any(reserva => reserva.EncerradaEmUtc != pedido.EncerradoEmUtc
                || !string.Equals(reserva.MotivoEncerramento, pedido.MotivoCancelamento, StringComparison.Ordinal)))
        {
            return EncerrarPedidoStatus.ReservasInconsistentes;
        }
        if (repeticao)
        {
            return EncerrarPedidoStatus.Repetido;
        }

        var transicao = expiracao ? TransicaoReservas.Expirar : TransicaoReservas.Liberar;
        if (CicloReservasProdutoAcabado.Aplicar(reservas, transicao, agoraUtc, motivo) != TransicaoReservasStatus.Sucesso)
        {
            return EncerrarPedidoStatus.ReservasInconsistentes;
        }

        return expiracao ? EncerrarPedidoStatus.Expirado : EncerrarPedidoStatus.Cancelado;
    }
}
