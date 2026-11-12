namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum SolicitarReenvioConfirmacaoEmailStatus
{
    InvalidEmail,
    RequestProcessed
}

public sealed class SolicitarReenvioConfirmacaoEmailResult
{
    private SolicitarReenvioConfirmacaoEmailResult(
        SolicitarReenvioConfirmacaoEmailStatus status)
    {
        Status = status;
    }

    public SolicitarReenvioConfirmacaoEmailStatus Status { get; }

    public static SolicitarReenvioConfirmacaoEmailResult InvalidEmail() =>
        new(SolicitarReenvioConfirmacaoEmailStatus.InvalidEmail);

    public static SolicitarReenvioConfirmacaoEmailResult RequestProcessed() =>
        new(SolicitarReenvioConfirmacaoEmailStatus.RequestProcessed);

    public override string ToString() => $"[{Status}]";
}
