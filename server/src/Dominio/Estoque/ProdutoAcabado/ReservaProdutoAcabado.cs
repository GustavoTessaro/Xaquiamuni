using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class ReservaProdutoAcabado
{
    public static TimeSpan Prazo => PrazoPagamentoCompra.Duracao;

    private ReservaProdutoAcabado(
        Guid pedidoId,
        Guid produtoVarianteId,
        Guid loteId,
        int quantidade,
        DateTimeOffset criadaEmUtc,
        DateTimeOffset expiraEmUtc)
    {
        Id = Guid.NewGuid();
        PedidoId = pedidoId;
        ProdutoVarianteId = produtoVarianteId;
        LoteId = loteId;
        Quantidade = quantidade;
        CriadaEmUtc = criadaEmUtc;
        ExpiraEmUtc = expiraEmUtc;
        Estado = EstadoReservaProdutoAcabado.Ativa;
    }

    public Guid Id { get; }
    public Guid PedidoId { get; }
    public Guid ProdutoVarianteId { get; }
    public Guid LoteId { get; }
    public int Quantidade { get; }
    public DateTimeOffset CriadaEmUtc { get; }
    public DateTimeOffset ExpiraEmUtc { get; }
    public EstadoReservaProdutoAcabado Estado { get; private set; }
    public DateTimeOffset? ConfirmadaEmUtc { get; private set; }
    public DateTimeOffset? EncerradaEmUtc { get; private set; }
    public string? MotivoEncerramento { get; private set; }

    public static ReservaProdutoAcabado Criar(
        Guid pedidoId,
        Guid produtoVarianteId,
        Guid loteId,
        int quantidade,
        DateTimeOffset criadaEmUtc,
        DateTimeOffset? expiraEmUtc = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pedidoId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(produtoVarianteId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(loteId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidade);
        ValidarUtc(criadaEmUtc, nameof(criadaEmUtc));
        if (expiraEmUtc is null && criadaEmUtc > DateTimeOffset.MaxValue.Subtract(Prazo))
        {
            throw new ArgumentOutOfRangeException(
                nameof(criadaEmUtc),
                "O prazo da reserva ultrapassa o intervalo de datas suportado.");
        }

        var prazoComum = expiraEmUtc ?? criadaEmUtc.Add(Prazo);
        ValidarUtc(prazoComum, nameof(expiraEmUtc));
        if (prazoComum <= criadaEmUtc || prazoComum - criadaEmUtc > Prazo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiraEmUtc),
                "O prazo deve ser posterior à criação e de no máximo 24 horas.");
        }

        return new ReservaProdutoAcabado(pedidoId, produtoVarianteId, loteId, quantidade, criadaEmUtc, prazoComum);
    }

    public bool EstaAtivaEm(DateTimeOffset instanteUtc)
    {
        ValidarUtc(instanteUtc, nameof(instanteUtc));
        return instanteUtc >= CriadaEmUtc
            && (Estado == EstadoReservaProdutoAcabado.Confirmada
                || (Estado == EstadoReservaProdutoAcabado.Ativa && instanteUtc < ExpiraEmUtc));
    }

    // A aplicação só deve chamar esta transição após autorização e verificação bancária.
    public bool ConfirmarCompromisso(DateTimeOffset confirmadaEmUtc)
    {
        ValidarUtc(confirmadaEmUtc, nameof(confirmadaEmUtc));
        if (confirmadaEmUtc < CriadaEmUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confirmadaEmUtc),
                "A confirmação não pode ser anterior à criação da reserva.");
        }

        if (Estado == EstadoReservaProdutoAcabado.Confirmada)
        {
            return false;
        }

        if (Estado != EstadoReservaProdutoAcabado.Ativa || confirmadaEmUtc >= ExpiraEmUtc)
        {
            throw new InvalidOperationException("Somente uma reserva ativa dentro do prazo pode ser confirmada.");
        }

        Estado = EstadoReservaProdutoAcabado.Confirmada;
        ConfirmadaEmUtc = confirmadaEmUtc;
        return true;
    }

    public bool Liberar(DateTimeOffset encerradaEmUtc, string? motivo)
    {
        if (!PodeEncerrar(EstadoReservaProdutoAcabado.Liberada, encerradaEmUtc))
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        Encerrar(EstadoReservaProdutoAcabado.Liberada, encerradaEmUtc, motivo.Trim());
        return true;
    }

    public bool Expirar(DateTimeOffset encerradaEmUtc)
    {
        if (!PodeEncerrar(EstadoReservaProdutoAcabado.Expirada, encerradaEmUtc))
        {
            return false;
        }

        Encerrar(EstadoReservaProdutoAcabado.Expirada, encerradaEmUtc);
        return true;
    }

    // A baixa física e a verificação de outras reservas cabem à transação que fecha a operação.
    public bool Consumir(
        ProdutoVariante variante,
        LoteProdutoAcabado lote,
        SaldoLoteProdutoAcabado saldo,
        DateOnly dataReferencia,
        DateTimeOffset encerradaEmUtc)
    {
        if (!PodeEncerrar(EstadoReservaProdutoAcabado.Consumida, encerradaEmUtc))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(variante);
        ArgumentNullException.ThrowIfNull(lote);
        ArgumentNullException.ThrowIfNull(saldo);
        if (variante.Id != ProdutoVarianteId || lote.ProdutoVarianteId != ProdutoVarianteId
            || lote.Id != LoteId || saldo.LoteId != LoteId || saldo.ProdutoVarianteId != ProdutoVarianteId)
        {
            throw new ArgumentException("A variante, o lote e o saldo devem corresponder à reserva.");
        }

        if (!variante.Ativa || !lote.EstaValidoEm(dataReferencia) || saldo.QuantidadeVendavel < Quantidade)
        {
            throw new InvalidOperationException(
                "O lote reservado ficou inelegível para consumo e exige resolução administrativa.");
        }

        Encerrar(EstadoReservaProdutoAcabado.Consumida, encerradaEmUtc);
        return true;
    }

    private bool PodeEncerrar(EstadoReservaProdutoAcabado destino, DateTimeOffset encerradaEmUtc)
    {
        ValidarUtc(encerradaEmUtc, nameof(encerradaEmUtc));
        if (encerradaEmUtc < (ConfirmadaEmUtc ?? CriadaEmUtc))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encerradaEmUtc),
                "O encerramento não pode ser anterior ao início do estado que o antecede.");
        }

        if (Estado == destino)
        {
            return false;
        }

        if (Estado == EstadoReservaProdutoAcabado.Confirmada)
        {
            if (destino == EstadoReservaProdutoAcabado.Expirada)
            {
                throw new InvalidOperationException("Uma reserva confirmada não expira.");
            }

            return true;
        }

        if (Estado != EstadoReservaProdutoAcabado.Ativa)
        {
            throw new InvalidOperationException("A reserva já foi encerrada em outro estado.");
        }

        if (destino == EstadoReservaProdutoAcabado.Expirada)
        {
            if (encerradaEmUtc < ExpiraEmUtc)
            {
                throw new InvalidOperationException("O prazo da reserva ainda não terminou.");
            }
        }
        else if (encerradaEmUtc >= ExpiraEmUtc)
        {
            throw new InvalidOperationException("O prazo da reserva terminou.");
        }
        else if (destino == EstadoReservaProdutoAcabado.Consumida)
        {
            throw new InvalidOperationException("Confirme a reserva antes de consumi-la.");
        }

        return true;
    }

    private void Encerrar(
        EstadoReservaProdutoAcabado estado,
        DateTimeOffset encerradaEmUtc,
        string? motivo = null)
    {
        Estado = estado;
        EncerradaEmUtc = encerradaEmUtc;
        MotivoEncerramento = motivo;
    }

    private static void ValidarUtc(DateTimeOffset instanteUtc, string parametro)
    {
        if (instanteUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante deve estar em UTC.", parametro);
        }
    }
}

public enum EstadoReservaProdutoAcabado
{
    Ativa = 0,
    Confirmada = 1,
    Liberada = 2,
    Expirada = 3,
    Consumida = 4
}
