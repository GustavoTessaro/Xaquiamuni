namespace Xaquiamuni.Aplicacao.Cadastro;

public enum CadastroClienteFailureCategory
{
    IDENTITY,
    PROFILE_ADDRESS,
    PERSISTENCE_TRANSACTION,
    CONFIGURATION_INFRASTRUCTURE
}

public enum CadastroClienteFailureStage
{
    USE_CASE_ENTRY,
    CPF_PROTECTION,
    ROLE_LOOKUP,
    TRANSACTION_BEGIN,
    IDENTITY_USER_CREATE,
    IDENTITY_USER_PERSISTENCE,
    IDENTITY_ROLE_ASSIGNMENT,
    IDENTITY_ROLE_PERSISTENCE,
    PROFILE_CREATE,
    ADDRESS_CREATE,
    PROFILE_ADDRESS_PERSISTENCE,
    TRANSACTION_SAVE,
    TRANSACTION_COMMIT
}

public static class CadastroClienteDiagnostics
{
    private const string CategoryKey = "Xaquiamuni.CadastroFailureCategory";
    private const string StageKey = "Xaquiamuni.CadastroFailureStage";
    private const string UnknownIdentityErrorCode = "UNKNOWN_IDENTITY_ERROR";

    private static readonly HashSet<string> AllowedIdentityErrorCodes = new(StringComparer.Ordinal)
    {
        "PasswordTooShort",
        "PasswordRequiresDigit",
        "PasswordRequiresUpper",
        "PasswordRequiresLower",
        "PasswordRequiresNonAlphanumeric",
        "PasswordRequiresUniqueChars",
        "InvalidUserName",
        "InvalidEmail"
    };

    public static IReadOnlyList<string> SanitizeIdentityErrorCodes(IEnumerable<string> codes) =>
        codes
            .Select(code => AllowedIdentityErrorCodes.Contains(code) ? code : UnknownIdentityErrorCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static void Attach(
        Exception exception,
        CadastroClienteFailureCategory category,
        CadastroClienteFailureStage stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[CategoryKey] = category;
        exception.Data[StageKey] = stage;
    }

    public static bool TryGet(
        Exception exception,
        out CadastroClienteFailureCategory category,
        out CadastroClienteFailureStage stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.Data[CategoryKey] is CadastroClienteFailureCategory storedCategory
            && Enum.IsDefined(storedCategory)
            && exception.Data[StageKey] is CadastroClienteFailureStage storedStage
            && Enum.IsDefined(storedStage))
        {
            category = storedCategory;
            stage = storedStage;
            return true;
        }

        category = default;
        stage = default;
        return false;
    }
}
