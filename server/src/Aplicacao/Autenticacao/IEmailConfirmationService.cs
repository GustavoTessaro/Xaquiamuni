namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IEmailConfirmationService
{
    Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<EmailConfirmationResult> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);
}
