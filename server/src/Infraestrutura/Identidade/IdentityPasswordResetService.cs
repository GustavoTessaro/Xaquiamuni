using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityPasswordResetService : IPasswordResetService
{
    private const int EmailMaxLength = 256;
    private const int PasswordMaxLength = 256;
    private const int TokenMaxLength = 4096;

    private readonly UserManager<XaquiamuniUser> _userManager;

    public IdentityPasswordResetService(UserManager<XaquiamuniUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Guid?> FindConfirmedUserIdByEmailAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidEmailAddress(email))
        {
            return null;
        }

        var validatedEmail = email!.Trim();
        var user = await _userManager.FindByEmailAsync(validatedEmail).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null || user.Id == Guid.Empty || !IsValidEmailAddress(user.Email))
        {
            return null;
        }

        var isEmailConfirmed = await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return isEmailConfirmed ? user.Id : null;
    }

    public async Task<PasswordResetTokenResult> GeneratePasswordResetTokenForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty)
        {
            return PasswordResetTokenResult.NotEligible();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null || user.Id == Guid.Empty || !IsValidEmailAddress(user.Email))
        {
            return PasswordResetTokenResult.NotEligible();
        }

        if (!await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return PasswordResetTokenResult.NotEligible();
        }

        var email = user.Email!;
        cancellationToken.ThrowIfCancellationRequested();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return PasswordResetTokenResult.Generated(email, token);
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(
        string? email,
        string? token,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length > PasswordMaxLength)
        {
            return PasswordResetResult.InvalidPassword();
        }

        if (string.IsNullOrWhiteSpace(token)
            || token.Length > TokenMaxLength
            || !IsValidEmailAddress(email))
        {
            return PasswordResetResult.InvalidRequest();
        }

        var validatedEmail = email!.Trim();
        var user = await _userManager.FindByEmailAsync(validatedEmail).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null
            || user.Id == Guid.Empty
            || !IsValidEmailAddress(user.Email)
            || !await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return PasswordResetResult.InvalidRequest();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword)
            .ConfigureAwait(false);
        return result.Succeeded
            ? PasswordResetResult.Success()
            : result.Errors.Any(error =>
                error.Code.StartsWith("Password", StringComparison.Ordinal))
                ? PasswordResetResult.InvalidPassword()
                : PasswordResetResult.InvalidRequest();
    }

    private static bool IsValidEmailAddress(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var value = email.Trim();
        if (value.Length > EmailMaxLength
            || !MailAddress.TryCreate(value, out var address)
            || !string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var domain = value.AsSpan(separator + 1);
        return domain.Contains('.')
            && domain[0] != '.'
            && domain[^1] != '.'
            && !domain.Contains("..", StringComparison.Ordinal);
    }
}
