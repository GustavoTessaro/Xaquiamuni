namespace Xaquiamuni.Dominio.Estoque;

public sealed class ContagemSemDiferencaProdutoAcabado
{
    private readonly byte[] _fingerprintRequisicao;

    private ContagemSemDiferencaProdutoAcabado()
    {
        _fingerprintRequisicao = [];
    }

    private ContagemSemDiferencaProdutoAcabado(
        Guid responsavelId,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao,
        DateTimeOffset registradoEmUtc)
    {
        ResponsavelId = responsavelId;
        ChaveIdempotencia = chaveIdempotencia;
        _fingerprintRequisicao = fingerprintRequisicao.ToArray();
        RegistradoEmUtc = registradoEmUtc;
    }

    public Guid ResponsavelId { get; private set; }
    public string ChaveIdempotencia { get; private set; } = null!;
    public byte[] FingerprintRequisicao => _fingerprintRequisicao.ToArray();
    public DateTimeOffset RegistradoEmUtc { get; private set; }

    public static ContagemSemDiferencaProdutoAcabado Criar(
        Guid responsavelId,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        DateTimeOffset registradoEmUtc)
    {
        if (responsavelId == Guid.Empty)
        {
            throw new ArgumentException("O responsável deve ter identificador válido.", nameof(responsavelId));
        }

        var chave = MovimentoProdutoAcabado.NormalizarChave(chaveIdempotencia);
        MovimentoProdutoAcabado.ValidarFingerprint(fingerprintRequisicao);
        if (registradoEmUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante de registro deve estar em UTC.", nameof(registradoEmUtc));
        }

        return new ContagemSemDiferencaProdutoAcabado(
            responsavelId,
            chave,
            fingerprintRequisicao!,
            registradoEmUtc);
    }
}
