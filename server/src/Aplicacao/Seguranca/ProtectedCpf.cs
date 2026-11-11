namespace Xaquiamuni.Aplicacao.Seguranca;

public sealed class ProtectedCpf
{
    public ProtectedCpf(
        string ciphertext,
        string nonce,
        string authenticationTag,
        string fingerprint,
        string encryptionKeyVersion,
        string fingerprintKeyVersion)
    {
        Ciphertext = ciphertext;
        Nonce = nonce;
        AuthenticationTag = authenticationTag;
        Fingerprint = fingerprint;
        EncryptionKeyVersion = encryptionKeyVersion;
        FingerprintKeyVersion = fingerprintKeyVersion;
    }

    public string Ciphertext { get; }
    public string Nonce { get; }
    public string AuthenticationTag { get; }
    public string Fingerprint { get; }
    public string EncryptionKeyVersion { get; }
    public string FingerprintKeyVersion { get; }

    public override string ToString() => "[Protected CPF]";
}
