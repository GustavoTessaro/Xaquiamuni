namespace Xaquiamuni.Dominio.Estoque;

public sealed class MovimentoInsumo
{
    public const int ChaveIdempotenciaMaxLength = 128;
    public const int MotivoMaxLength = 120;
    public const int ObservacaoMaxLength = 500;

    private MovimentoInsumo()
    {
    }

    private MovimentoInsumo(
        Guid id,
        Guid insumoId,
        Guid? loteId,
        Guid responsavelId,
        TipoMovimentoInsumo tipo,
        decimal quantidadeOriginal,
        UnidadeFisicaEstoque? unidadeOriginal,
        Guid? formaCompraId,
        string? formaCompraNome,
        QuantidadeEstoque? fatorCompra,
        QuantidadeEstoque quantidadeBase,
        QuantidadeEstoque saldoAnterior,
        QuantidadeEstoque saldoPosterior,
        CustoAquisicaoInsumo custo,
        string motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao)
    {
        Id = id;
        InsumoId = insumoId;
        LoteId = loteId;
        ResponsavelId = responsavelId;
        Tipo = tipo;
        QuantidadeOriginal = quantidadeOriginal;
        UnidadeOriginal = unidadeOriginal;
        FormaCompraId = formaCompraId;
        FormaCompraNome = formaCompraNome;
        FatorCompra = fatorCompra;
        QuantidadeBase = quantidadeBase;
        SaldoAnterior = saldoAnterior;
        SaldoPosterior = saldoPosterior;
        CustoTotal = custo.Total;
        CustoPorUnidadeBase = custo.PorUnidadeBase;
        Motivo = motivo;
        Observacao = observacao;
        RegistradoEmUtc = registradoEmUtc;
        ChaveIdempotencia = chaveIdempotencia;
        FingerprintRequisicao = fingerprintRequisicao;
    }

    public Guid Id { get; private set; }
    public Guid InsumoId { get; private set; }
    public Guid? LoteId { get; private set; }
    public Guid ResponsavelId { get; private set; }
    public TipoMovimentoInsumo Tipo { get; private set; }
    public decimal QuantidadeOriginal { get; private set; }
    public UnidadeFisicaEstoque? UnidadeOriginal { get; private set; }
    public Guid? FormaCompraId { get; private set; }
    public string? FormaCompraNome { get; private set; }
    public QuantidadeEstoque? FatorCompra { get; private set; }
    public QuantidadeEstoque QuantidadeBase { get; private set; } = null!;
    public QuantidadeEstoque SaldoAnterior { get; private set; } = null!;
    public QuantidadeEstoque SaldoPosterior { get; private set; } = null!;
    public decimal? CustoTotal { get; private set; }
    public decimal? CustoPorUnidadeBase { get; private set; }
    public string Motivo { get; private set; } = null!;
    public string? Observacao { get; private set; }
    public DateTimeOffset RegistradoEmUtc { get; private set; }
    public string ChaveIdempotencia { get; private set; } = null!;
    public byte[] FingerprintRequisicao { get; private set; } = null!;

    internal static MovimentoInsumo CriarEntrada(
        InsumoEstoque insumo,
        LoteInsumo? lote,
        Guid responsavelId,
        TipoMovimentoInsumo tipo,
        decimal quantidadeOriginal,
        UnidadeFisicaEstoque? unidadeOriginal,
        ResultadoConversaoCompraInsumo? conversaoCompra,
        QuantidadeEstoque quantidadeBase,
        QuantidadeEstoque saldoAnterior,
        CustoAquisicaoInsumo custo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao)
    {
        ArgumentNullException.ThrowIfNull(insumo);
        ArgumentNullException.ThrowIfNull(quantidadeBase);
        ArgumentNullException.ThrowIfNull(saldoAnterior);
        ArgumentNullException.ThrowIfNull(custo);
        ArgumentNullException.ThrowIfNull(fingerprintRequisicao);

        if (insumo.Id == Guid.Empty || responsavelId == Guid.Empty)
        {
            throw new ArgumentException("Insumo e responsável devem ter identificadores válidos.");
        }

        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), "Tipo de movimento não implementado.");
        }

        quantidadeBase.ExigirPositiva(nameof(quantidadeBase));
        if (quantidadeBase.UnidadeBase != insumo.UnidadeBase)
        {
            throw new ArgumentException("A quantidade deve estar na unidade-base do insumo.", nameof(quantidadeBase));
        }

        if (insumo.ControlaLote != (lote is not null)
            || lote is not null && lote.InsumoId != insumo.Id)
        {
            throw new ArgumentException("O lote deve corresponder à configuração e ao insumo.", nameof(lote));
        }

        if (registradoEmUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante de registro deve estar em UTC.", nameof(registradoEmUtc));
        }

        var key = NormalizarObrigatorio(
            chaveIdempotencia,
            ChaveIdempotenciaMaxLength,
            nameof(chaveIdempotencia));
        var normalizedNote = string.IsNullOrWhiteSpace(observacao)
            ? null
            : NormalizarObrigatorio(observacao, ObservacaoMaxLength, nameof(observacao));
        var reason = tipo switch
        {
            TipoMovimentoInsumo.EstoqueInicial => "Estoque inicial",
            TipoMovimentoInsumo.Aquisicao => "Aquisição",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo))
        };
        var factor = conversaoCompra?.FatorAplicado;

        decimal saldoPosteriorValor;
        try
        {
            saldoPosteriorValor = checked(saldoAnterior.ValorBase + quantidadeBase.ValorBase);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidadeBase),
                "A entrada excede o limite de quantidade-base.");
        }

        var saldoPosterior = QuantidadeEstoque.Criar(saldoPosteriorValor, insumo.UnidadeBase);
        return new MovimentoInsumo(
            Guid.NewGuid(),
            insumo.Id,
            lote?.Id,
            responsavelId,
            tipo,
            quantidadeOriginal,
            unidadeOriginal,
            conversaoCompra?.FormaCompraId,
            conversaoCompra?.NomeFormaCompra,
            factor,
            quantidadeBase,
            saldoAnterior,
            saldoPosterior,
            custo,
            reason,
            normalizedNote,
            registradoEmUtc,
            key,
            [.. fingerprintRequisicao]);
    }

    private static string NormalizarObrigatorio(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O valor é obrigatório.", paramName);
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"O valor não pode exceder {maxLength} caracteres.", paramName);
        }

        return normalized;
    }
}
