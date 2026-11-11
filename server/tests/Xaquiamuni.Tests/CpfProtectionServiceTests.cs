using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Seguranca;

namespace Xaquiamuni.Tests;

public class CpfProtectionServiceTests
{
    [Fact]
    public void ProtectAndRecover_RoundTripsValidatedCpf()
    {
        var service = CreateService();
        var cpf = CreateCpf("529.982.247-25");

        var protectedCpf = service.Protect(cpf);
        var recoveredCpf = service.RecoverForAuthorizedUse(protectedCpf);

        Assert.Equal(cpf, recoveredCpf);
        Assert.Equal("enc-v1", protectedCpf.EncryptionKeyVersion);
        Assert.Equal("fp-v1", protectedCpf.FingerprintKeyVersion);
        Assert.Equal("[Protected CPF]", protectedCpf.ToString());
    }

    [Fact]
    public void Protect_UsesDifferentCiphertextAndNonceForSameCpf()
    {
        var service = CreateService();
        var cpf = CreateCpf("52998224725");

        var first = service.Protect(cpf);
        var second = service.Protect(cpf);

        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Fingerprint_IsDeterministicForSameCpf()
    {
        var service = CreateService();
        var formatted = CreateCpf("529.982.247-25");
        var unformatted = CreateCpf("52998224725");

        Assert.Equal(
            service.CreateFingerprintForLookup(formatted),
            service.CreateFingerprintForLookup(unformatted));
    }

    [Fact]
    public void Fingerprint_DiffersForDifferentCpf()
    {
        var service = CreateService();

        Assert.NotEqual(
            service.CreateFingerprintForLookup(CreateCpf("52998224725")),
            service.CreateFingerprintForLookup(CreateCpf("11144477735")));
    }

    [Fact]
    public void Recover_RejectsTamperedCiphertext()
    {
        var service = CreateService();
        var protectedCpf = service.Protect(CreateCpf("52998224725"));
        var ciphertext = Convert.FromBase64String(protectedCpf.Ciphertext);
        ciphertext[0] ^= 0x01;
        var tampered = CopyWith(protectedCpf, ciphertext: Convert.ToBase64String(ciphertext));

        var exception = Assert.Throws<CryptographicException>(
            () => service.RecoverForAuthorizedUse(tampered));

        Assert.Equal("The protected CPF data could not be authenticated.", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsEncryptionKeyWithInvalidLength()
    {
        var options = CreateOptions(encryptionKey: RandomNumberGenerator.GetBytes(16));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new AesGcmCpfProtectionService(Options.Create(options)));

        Assert.Equal("CPF protection keys must contain exactly 32 bytes.", exception.Message);
    }

    [Theory]
    [InlineData("unknown-encryption", "fp-v1")]
    [InlineData("enc-v1", "unknown-fingerprint")]
    public void Recover_RejectsUnknownKeyVersions(string encryptionVersion, string fingerprintVersion)
    {
        var service = CreateService();
        var protectedCpf = CopyWith(
            service.Protect(CreateCpf("52998224725")),
            encryptionKeyVersion: encryptionVersion,
            fingerprintKeyVersion: fingerprintVersion);

        Assert.Throws<InvalidOperationException>(
            () => service.RecoverForAuthorizedUse(protectedCpf));
    }

    [Fact]
    public void Constructor_RejectsMissingKeyConfiguration()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new AesGcmCpfProtectionService(Options.Create(new CpfProtectionOptions())));

        Assert.Equal(
            "CPF protection key versions must be configured using letters, digits, '-' or '_'.",
            exception.Message);
    }

    [Fact]
    public void Constructor_RejectsReusedEncryptionAndFingerprintKeys()
    {
        var sharedKey = RandomNumberGenerator.GetBytes(32);
        var options = CreateOptions(encryptionKey: sharedKey, fingerprintKey: sharedKey);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new AesGcmCpfProtectionService(Options.Create(options)));

        Assert.Equal("CPF encryption and fingerprint keys must be independent.", exception.Message);
    }

    private static AesGcmCpfProtectionService CreateService() =>
        new(Options.Create(CreateOptions()));

    private static CpfProtectionOptions CreateOptions(
        byte[]? encryptionKey = null,
        byte[]? fingerprintKey = null)
    {
        return new CpfProtectionOptions
        {
            CurrentEncryptionKeyVersion = "enc-v1",
            EncryptionKeys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["enc-v1"] = Convert.ToBase64String(encryptionKey ?? RandomNumberGenerator.GetBytes(32))
            },
            CurrentFingerprintKeyVersion = "fp-v1",
            FingerprintKeys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["fp-v1"] = Convert.ToBase64String(fingerprintKey ?? RandomNumberGenerator.GetBytes(32))
            }
        };
    }

    private static Cpf CreateCpf(string value)
    {
        Assert.True(Cpf.TryCreate(value, out var cpf));
        return Assert.IsType<Cpf>(cpf);
    }

    private static ProtectedCpf CopyWith(
        ProtectedCpf source,
        string? ciphertext = null,
        string? encryptionKeyVersion = null,
        string? fingerprintKeyVersion = null) =>
        new(
            ciphertext ?? source.Ciphertext,
            source.Nonce,
            source.AuthenticationTag,
            source.Fingerprint,
            encryptionKeyVersion ?? source.EncryptionKeyVersion,
            fingerprintKeyVersion ?? source.FingerprintKeyVersion);
}
