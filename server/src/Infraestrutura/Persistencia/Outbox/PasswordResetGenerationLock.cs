namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

internal static class PasswordResetGenerationLock
{
    public static string GetResourceName(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        return $"PasswordResetGeneration:{userId:N}";
    }
}
