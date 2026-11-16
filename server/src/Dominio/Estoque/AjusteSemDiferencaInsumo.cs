namespace Xaquiamuni.Dominio.Estoque;

public sealed class AjusteSemDiferencaInsumo
{
    private AjusteSemDiferencaInsumo()
    {
    }

    private AjusteSemDiferencaInsumo(
        Guid responsavelId,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao,
        DateTimeOffset registradoEmUtc)
    {
        ResponsavelId = responsavelId;
        ChaveIdempotencia = chaveIdempotencia;
        FingerprintRequisicao = fingerprintRequisicao;
        RegistradoEmUtc = registradoEmUtc;
    }

    public Guid ResponsavelId { get; private set; }
    public string ChaveIdempotencia { get; private set; } = null!;
    public byte[] FingerprintRequisicao { get; private set; } = null!;
    public DateTimeOffset RegistradoEmUtc { get; private set; }

    internal static AjusteSemDiferencaInsumo Criar(
        Guid responsavelId,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao,
        DateTimeOffset registradoEmUtc)
    {
        if (responsavelId == Guid.Empty)
        {
            throw new ArgumentException("O responsável deve ter identificador válido.", nameof(responsavelId));
        }

        if (string.IsNullOrWhiteSpace(chaveIdempotencia))
        {
            throw new ArgumentException("A chave de idempotência é obrigatória.", nameof(chaveIdempotencia));
        }

        var chave = string.Join(
            ' ',
            chaveIdempotencia.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (chave.Length > MovimentoInsumo.ChaveIdempotenciaMaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chaveIdempotencia),
                "A chave de idempotência excede o limite.");
        }

        ArgumentNullException.ThrowIfNull(fingerprintRequisicao);
        if (fingerprintRequisicao.Length != 32)
        {
            throw new ArgumentException("O fingerprint deve conter 32 bytes.", nameof(fingerprintRequisicao));
        }

        if (registradoEmUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante de registro deve estar em UTC.", nameof(registradoEmUtc));
        }

        return new AjusteSemDiferencaInsumo(
            responsavelId,
            chave,
            [.. fingerprintRequisicao],
            registradoEmUtc);
    }
}
