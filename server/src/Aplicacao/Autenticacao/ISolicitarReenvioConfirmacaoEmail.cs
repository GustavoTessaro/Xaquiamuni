namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface ISolicitarReenvioConfirmacaoEmail
{
    Task<SolicitarReenvioConfirmacaoEmailResult> ExecuteAsync(
        string? email,
        CancellationToken cancellationToken = default);
}
