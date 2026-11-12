namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface ICredentialGenerationFingerprint
{
    byte[] Compute(string securityStamp);

    bool Matches(string securityStamp, ReadOnlySpan<byte> fingerprint);
}
