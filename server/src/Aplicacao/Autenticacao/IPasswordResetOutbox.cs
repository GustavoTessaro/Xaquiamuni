namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IPasswordResetOutbox
{
    Task<PasswordResetOutboxResult> RequestAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
