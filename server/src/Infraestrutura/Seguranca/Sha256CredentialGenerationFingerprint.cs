using System.Security.Cryptography;
using System.Text;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Seguranca;

public sealed class Sha256CredentialGenerationFingerprint : ICredentialGenerationFingerprint
{
    private static readonly byte[] Purpose =
        Encoding.UTF8.GetBytes("Xaquiamuni.PasswordReset.CredentialGeneration.v1\0");

    public byte[] Compute(string securityStamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        var stampBytes = Encoding.UTF8.GetBytes(securityStamp);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Purpose);
            hash.AppendData(stampBytes);
            return hash.GetHashAndReset();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stampBytes);
        }
    }

    public bool Matches(string securityStamp, ReadOnlySpan<byte> fingerprint)
    {
        if (fingerprint.Length != SHA256.HashSizeInBytes)
        {
            return false;
        }

        var computed = Compute(securityStamp);
        try
        {
            return CryptographicOperations.FixedTimeEquals(computed, fingerprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computed);
        }
    }
}
