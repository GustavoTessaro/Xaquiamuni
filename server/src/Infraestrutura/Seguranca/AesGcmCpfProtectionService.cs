using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Infraestrutura.Seguranca;

public sealed class AesGcmCpfProtectionService : ICpfProtectionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string AssociatedDataPrefix = "Xaquiamuni.CPF:";
    private readonly string _currentEncryptionKeyVersion;
    private readonly string _currentFingerprintKeyVersion;
    private readonly IReadOnlyDictionary<string, byte[]> _encryptionKeys;
    private readonly IReadOnlyDictionary<string, byte[]> _fingerprintKeys;

    public AesGcmCpfProtectionService(IOptions<CpfProtectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configuration = options.Value;
        _currentEncryptionKeyVersion = ValidateVersion(configuration.CurrentEncryptionKeyVersion);
        _currentFingerprintKeyVersion = ValidateVersion(configuration.CurrentFingerprintKeyVersion);
        _encryptionKeys = DecodeKeys(configuration.EncryptionKeys, _currentEncryptionKeyVersion);
        _fingerprintKeys = DecodeKeys(configuration.FingerprintKeys, _currentFingerprintKeyVersion);
        if (_encryptionKeys.Values.Any(encryptionKey =>
                _fingerprintKeys.Values.Any(fingerprintKey =>
                    CryptographicOperations.FixedTimeEquals(encryptionKey, fingerprintKey))))
        {
            throw new InvalidOperationException("CPF encryption and fingerprint keys must be independent.");
        }
    }

    public CpfProtegido Protect(Cpf cpf)
    {
        ArgumentNullException.ThrowIfNull(cpf);

        var plaintext = Encoding.UTF8.GetBytes(cpf.GetNormalizedDigitsForProtection());
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(_encryptionKeys[_currentEncryptionKeyVersion], TagSize);
            aes.Encrypt(
                nonce,
                plaintext,
                ciphertext,
                tag,
                GetAssociatedData(_currentEncryptionKeyVersion));

            var fingerprint = CreateFingerprint(cpf, _currentFingerprintKeyVersion);
            return new CpfProtegido(
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag),
                fingerprint,
                _currentEncryptionKeyVersion,
                _currentFingerprintKeyVersion);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Cpf RecoverForAuthorizedUse(CpfProtegido protectedCpf)
    {
        ArgumentNullException.ThrowIfNull(protectedCpf);

        if (!_encryptionKeys.TryGetValue(protectedCpf.EncryptionKeyVersion, out var key))
        {
            throw new InvalidOperationException("The protected CPF uses an unknown encryption key version.");
        }

        if (!_fingerprintKeys.ContainsKey(protectedCpf.FingerprintKeyVersion))
        {
            throw new InvalidOperationException("The protected CPF uses an unknown fingerprint key version.");
        }

        var ciphertext = DecodeBase64(protectedCpf.Ciphertext);
        var nonce = DecodeBase64(protectedCpf.Nonce);
        var tag = DecodeBase64(protectedCpf.AuthenticationTag);
        if (nonce.Length != NonceSize || tag.Length != TagSize || ciphertext.Length != 11)
        {
            throw new CryptographicException("The protected CPF data is invalid.");
        }

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext,
                GetAssociatedData(protectedCpf.EncryptionKeyVersion));

            var digits = Encoding.UTF8.GetString(plaintext);
            if (!Cpf.TryCreate(digits, out var cpf))
            {
                throw new CryptographicException("The protected CPF data is invalid.");
            }

            return cpf!;
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("The protected CPF data could not be authenticated.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public string CreateFingerprintForLookup(Cpf cpf)
    {
        ArgumentNullException.ThrowIfNull(cpf);
        return CreateFingerprint(cpf, _currentFingerprintKeyVersion);
    }

    private string CreateFingerprint(Cpf cpf, string keyVersion)
    {
        var digits = Encoding.UTF8.GetBytes(cpf.GetNormalizedDigitsForProtection());
        try
        {
            var hash = HMACSHA256.HashData(_fingerprintKeys[keyVersion], digits);
            return Convert.ToHexString(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digits);
        }
    }

    private static byte[] GetAssociatedData(string keyVersion) =>
        Encoding.UTF8.GetBytes($"{AssociatedDataPrefix}{keyVersion}");

    private static string ValidateVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)
            || version.Length > 32
            || version.Any(character =>
                character is not (>= 'a' and <= 'z')
                    and not (>= 'A' and <= 'Z')
                    and not (>= '0' and <= '9')
                    and not '-' and not '_'))
        {
            throw new InvalidOperationException("CPF protection key versions must be configured using letters, digits, '-' or '_'.");
        }

        return version;
    }

    private static IReadOnlyDictionary<string, byte[]> DecodeKeys(
        IReadOnlyDictionary<string, string>? configuredKeys,
        string requiredVersion)
    {
        if (configuredKeys is null || configuredKeys.Count == 0)
        {
            throw new InvalidOperationException("CPF protection keys must be configured.");
        }

        var decodedKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (version, encodedKey) in configuredKeys)
        {
            ValidateVersion(version);
            if (string.IsNullOrWhiteSpace(encodedKey))
            {
                throw new InvalidOperationException("CPF protection keys must be configured.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(encodedKey);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("CPF protection keys must be valid Base64 values.");
            }

            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidOperationException("CPF protection keys must contain exactly 32 bytes.");
            }

            decodedKeys.Add(version, key);
        }

        if (!decodedKeys.ContainsKey(requiredVersion))
        {
            throw new InvalidOperationException("The current CPF protection key version is not configured.");
        }

        return decodedKeys;
    }

    private static byte[] DecodeBase64(string? value)
    {
        try
        {
            return Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new CryptographicException("The protected CPF data is invalid.");
        }
    }
}
