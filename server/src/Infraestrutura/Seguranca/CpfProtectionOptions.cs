namespace Xaquiamuni.Infraestrutura.Seguranca;

public sealed class CpfProtectionOptions
{
    public string? CurrentEncryptionKeyVersion { get; set; }
    public Dictionary<string, string> EncryptionKeys { get; set; } = new(StringComparer.Ordinal);
    public string? CurrentFingerprintKeyVersion { get; set; }
    public Dictionary<string, string> FingerprintKeys { get; set; } = new(StringComparer.Ordinal);
}
