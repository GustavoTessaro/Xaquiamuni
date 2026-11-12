namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface ISolicitarRecuperacaoSenha
{
    Task<SolicitarRecuperacaoSenhaResult> ExecuteAsync(
        string? email,
        CancellationToken cancellationToken = default);
}
