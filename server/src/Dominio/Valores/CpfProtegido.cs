namespace Xaquiamuni.Dominio.Valores;

public sealed class CpfProtegido
{
    public CpfProtegido(
        string ciphertext,
        string nonce,
        string authenticationTag,
        string fingerprint,
        string encryptionKeyVersion,
        string fingerprintKeyVersion)
    {
        Ciphertext = ValidateBase64(ciphertext, 11, "O CPF protegido é inválido.");
        Nonce = ValidateBase64(nonce, 12, "O CPF protegido é inválido.");
        AuthenticationTag = ValidateBase64(authenticationTag, 16, "O CPF protegido é inválido.");
        Fingerprint = ValidateFingerprint(fingerprint);
        EncryptionKeyVersion = ValidateKeyVersion(encryptionKeyVersion);
        FingerprintKeyVersion = ValidateKeyVersion(fingerprintKeyVersion);
    }

    public string Ciphertext { get; }
    public string Nonce { get; }
    public string AuthenticationTag { get; }
    public string Fingerprint { get; }
    public string EncryptionKeyVersion { get; }
    public string FingerprintKeyVersion { get; }

    public override string ToString() => "[CPF protegido]";

    private static string ValidateBase64(string? value, int expectedByteLength, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(errorMessage);
        }

        try
        {
            if (Convert.FromBase64String(value).Length != expectedByteLength)
            {
                throw new ArgumentException(errorMessage);
            }
        }
        catch (FormatException)
        {
            throw new ArgumentException(errorMessage);
        }

        return value;
    }

    private static string ValidateFingerprint(string? fingerprint)
    {
        if (fingerprint is null
            || fingerprint.Length != 64
            || fingerprint.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException("A impressão digital do CPF protegido é inválida.");
        }

        return fingerprint;
    }

    private static string ValidateKeyVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)
            || version.Length > 32
            || version.Any(character =>
                character is not (>= 'a' and <= 'z')
                    and not (>= 'A' and <= 'Z')
                    and not (>= '0' and <= '9')
                    and not '-' and not '_'))
        {
            throw new ArgumentException("A versão da chave do CPF protegido é inválida.");
        }

        return version;
    }
}
