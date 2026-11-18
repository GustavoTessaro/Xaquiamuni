using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public interface ICicloReservasProdutoAcabado
{
    // O chamador futuro deve verificar autorização e evidência bancária antes de confirmar.
    Task<TransicaoReservasStatus> ConfirmarCompromissoAsync(
        Guid pedidoId, CancellationToken cancellationToken = default);

    Task<TransicaoReservasStatus> LiberarAsync(
        Guid pedidoId, string? motivo, CancellationToken cancellationToken = default);

    Task<TransicaoReservasStatus> ExpirarAsync(
        Guid pedidoId, CancellationToken cancellationToken = default);
}

public enum TransicaoReservasStatus
{
    Sucesso,
    Repetida,
    SolicitacaoInvalida,
    ReservasNaoEncontradas,
    EstadoIncompativel,
    PrazoEncerrado,
    PrazoNaoEncerrado,
    ConflitoIdempotencia,
    ConflitoConcorrencia
}

public enum TransicaoReservas
{
    Confirmar,
    Liberar,
    Expirar
}

public static class CicloReservasProdutoAcabado
{
    public static TransicaoReservasStatus Aplicar(
        IReadOnlyList<ReservaProdutoAcabado> reservas,
        TransicaoReservas transicao,
        DateTimeOffset agoraUtc,
        string? motivo = null)
    {
        if (transicao == TransicaoReservas.Liberar && string.IsNullOrWhiteSpace(motivo))
        {
            return TransicaoReservasStatus.SolicitacaoInvalida;
        }

        if (reservas.Count == 0)
        {
            return TransicaoReservasStatus.ReservasNaoEncontradas;
        }

        var prazoComum = reservas[0].ExpiraEmUtc;
        if (reservas.Any(reserva => reserva.ExpiraEmUtc != prazoComum))
        {
            return TransicaoReservasStatus.EstadoIncompativel;
        }

        var destino = transicao switch
        {
            TransicaoReservas.Confirmar => EstadoReservaProdutoAcabado.Confirmada,
            TransicaoReservas.Liberar => EstadoReservaProdutoAcabado.Liberada,
            TransicaoReservas.Expirar => EstadoReservaProdutoAcabado.Expirada,
            _ => throw new ArgumentOutOfRangeException(nameof(transicao))
        };
        motivo = motivo?.Trim();
        if (reservas.All(reserva => reserva.Estado == destino))
        {
            return transicao == TransicaoReservas.Liberar
                && reservas.Any(reserva => !string.Equals(reserva.MotivoEncerramento, motivo, StringComparison.Ordinal))
                ? TransicaoReservasStatus.ConflitoIdempotencia
                : TransicaoReservasStatus.Repetida;
        }

        if (reservas.Any(reserva => reserva.Estado != EstadoReservaProdutoAcabado.Ativa
            && !(transicao == TransicaoReservas.Liberar && reserva.Estado == EstadoReservaProdutoAcabado.Confirmada)))
        {
            return TransicaoReservasStatus.EstadoIncompativel;
        }

        if (agoraUtc.Offset != TimeSpan.Zero
            || reservas.Any(reserva => agoraUtc < (reserva.ConfirmadaEmUtc ?? reserva.CriadaEmUtc)))
        {
            return TransicaoReservasStatus.SolicitacaoInvalida;
        }

        if (transicao == TransicaoReservas.Expirar)
        {
            if (agoraUtc < prazoComum)
            {
                return TransicaoReservasStatus.PrazoNaoEncerrado;
            }
        }
        else if (agoraUtc >= prazoComum && reservas.Any(reserva => reserva.Estado == EstadoReservaProdutoAcabado.Ativa))
        {
            return TransicaoReservasStatus.PrazoEncerrado;
        }

        foreach (var reserva in reservas)
        {
            switch (transicao)
            {
                case TransicaoReservas.Confirmar: reserva.ConfirmarCompromisso(agoraUtc); break;
                case TransicaoReservas.Liberar: reserva.Liberar(agoraUtc, motivo); break;
                case TransicaoReservas.Expirar: reserva.Expirar(agoraUtc); break;
            }
        }

        return TransicaoReservasStatus.Sucesso;
    }
}
