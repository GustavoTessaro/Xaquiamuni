using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityPasswordResetService : IPasswordResetService
{
    private const int EmailMaxLength = 256;
    private const int PasswordMaxLength = 256;
    private const int TokenMaxLength = 4096;
    private const int SecurityStampMaxLength = 256;

    private readonly UserManager<XaquiamuniUser> _userManager;
    private readonly ICredentialGenerationFingerprint _credentialGenerationFingerprint;
    private readonly IPasswordResetTransactionCoordinator _transactionCoordinator;

    internal IdentityPasswordResetService(
        UserManager<XaquiamuniUser> userManager,
        ICredentialGenerationFingerprint credentialGenerationFingerprint,
        IPasswordResetTransactionCoordinator transactionCoordinator)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _credentialGenerationFingerprint = credentialGenerationFingerprint
            ?? throw new ArgumentNullException(nameof(credentialGenerationFingerprint));
        _transactionCoordinator = transactionCoordinator
            ?? throw new ArgumentNullException(nameof(transactionCoordinator));
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
        ReadOnlyMemory<byte>? credentialGenerationFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty
            || credentialGenerationFingerprint is not ReadOnlyMemory<byte> expectedFingerprint
            || expectedFingerprint.Length != SHA256.HashSizeInBytes)
        {
            return PasswordResetTokenResult.InvalidRequest();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null
            || user.Id != userId
            || !IsValidEmailAddress(user.Email)
            || !IsValidSecurityStamp(user.SecurityStamp))
        {
            return PasswordResetTokenResult.NotEligible();
        }

        if (!await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return PasswordResetTokenResult.NotEligible();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var currentFingerprint = _credentialGenerationFingerprint.Compute(user.SecurityStamp!);
        bool matches;
        try
        {
            if (currentFingerprint.Length != SHA256.HashSizeInBytes)
            {
                throw new InvalidOperationException(
                    "The credential generation fingerprint has an invalid size.");
            }

            matches = CryptographicOperations.FixedTimeEquals(
                currentFingerprint,
                expectedFingerprint.Span);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentFingerprint);
        }

        if (!matches)
        {
            return PasswordResetTokenResult.CredentialGenerationMismatch();
        }

        var email = user.Email!;
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
        var normalizedEmail = _userManager.NormalizeEmail(validatedEmail);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return PasswordResetResult.InvalidRequest();
        }

        var userId = await _transactionCoordinator
            .FindUserIdByNormalizedEmailAsync(normalizedEmail, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (userId is not Guid validUserId || validUserId == Guid.Empty)
        {
            return PasswordResetResult.InvalidRequest();
        }

        await using var transaction = await _transactionCoordinator
            .BeginAsync(validUserId, cancellationToken)
            .ConfigureAwait(false);

        XaquiamuniUser? user = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            user = await _userManager.FindByIdAsync(validUserId.ToString())
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (user is null
                || user.Id != validUserId
                || !await transaction.ReloadUserAsync(user, cancellationToken).ConfigureAwait(false)
                || !IsValidEmailAddress(user.Email)
                || !string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal)
                || !await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
            {
                await RollbackAndRefreshAsync(transaction, user).ConfigureAwait(false);
                return PasswordResetResult.InvalidRequest();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Succeeded)
            {
                await RollbackAndRefreshAsync(transaction, user).ConfigureAwait(false);
                return result.Errors.Any(error =>
                    error.Code.StartsWith("Password", StringComparison.Ordinal))
                    ? PasswordResetResult.InvalidPassword()
                    : PasswordResetResult.InvalidRequest();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return PasswordResetResult.Success();
        }
        catch
        {
            await RollbackAndRefreshAsync(transaction, user).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task RollbackAndRefreshAsync(
        IPasswordResetTransaction transaction,
        XaquiamuniUser? user)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        if (user is not null)
        {
            await transaction.ReloadUserAsync(user, CancellationToken.None).ConfigureAwait(false);
        }
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

    private static bool IsValidSecurityStamp(string? securityStamp) =>
        !string.IsNullOrWhiteSpace(securityStamp)
        && securityStamp.Length <= SecurityStampMaxLength
        && !securityStamp.Any(char.IsControl);
}
