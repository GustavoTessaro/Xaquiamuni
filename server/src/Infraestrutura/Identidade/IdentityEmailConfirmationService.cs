using Microsoft.AspNetCore.Identity;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityEmailConfirmationService : IEmailConfirmationService
{
    private readonly UserManager<XaquiamuniUser> _userManager;

    public IdentityEmailConfirmationService(UserManager<XaquiamuniUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByEmailAsync(email.Trim()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
        {
            return EmailConfirmationTokenResult.UserNotFound();
        }

        if (await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return EmailConfirmationTokenResult.AlreadyConfirmed();
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
        return EmailConfirmationTokenResult.Generated(token);
    }

    public async Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
        {
            return EmailConfirmationTokenResult.UserNotFound();
        }

        if (await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return EmailConfirmationTokenResult.AlreadyConfirmed();
        }

        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return EmailConfirmationTokenResult.EmailMissing();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return EmailConfirmationTokenResult.GeneratedForUser(email, token);
    }

    public async Task<EmailConfirmationResult> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByEmailAsync(email.Trim()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
        {
            return EmailConfirmationResult.UserNotFound();
        }

        if (await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return EmailConfirmationResult.AlreadyConfirmed();
        }

        var result = await _userManager.ConfirmEmailAsync(user, token).ConfigureAwait(false);
        return result.Succeeded
            ? EmailConfirmationResult.Confirmed()
            : EmailConfirmationResult.InvalidOrExpiredToken();
    }
}
