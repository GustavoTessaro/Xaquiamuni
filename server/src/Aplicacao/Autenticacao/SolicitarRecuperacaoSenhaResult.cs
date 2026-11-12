namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum SolicitarRecuperacaoSenhaStatus
{
    InvalidEmail,
    RequestProcessed
}

public sealed class SolicitarRecuperacaoSenhaResult
{
    private SolicitarRecuperacaoSenhaResult(SolicitarRecuperacaoSenhaStatus status)
    {
        Status = status;
    }

    public SolicitarRecuperacaoSenhaStatus Status { get; }

    public static SolicitarRecuperacaoSenhaResult InvalidEmail() =>
        new(SolicitarRecuperacaoSenhaStatus.InvalidEmail);

    public static SolicitarRecuperacaoSenhaResult RequestProcessed() =>
        new(SolicitarRecuperacaoSenhaStatus.RequestProcessed);

    public override string ToString() => $"[{Status}]";
}
