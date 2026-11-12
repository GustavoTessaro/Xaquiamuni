namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IPasswordResetService
{
    Task<Guid?> FindConfirmedUserIdByEmailAsync(
        string? email,
        CancellationToken cancellationToken = default);

    Task<PasswordResetTokenResult> GeneratePasswordResetTokenForUserAsync(
        Guid userId,
        ReadOnlyMemory<byte>? credentialGenerationFingerprint,
        CancellationToken cancellationToken = default);

    Task<PasswordResetResult> ResetPasswordAsync(
        string? email,
        string? token,
        string? newPassword,
        CancellationToken cancellationToken = default);
}
