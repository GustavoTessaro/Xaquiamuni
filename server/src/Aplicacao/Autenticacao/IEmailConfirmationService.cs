namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IEmailConfirmationService
{
    Task<Guid?> FindUnconfirmedUserIdByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<EmailConfirmationResult> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);
}
