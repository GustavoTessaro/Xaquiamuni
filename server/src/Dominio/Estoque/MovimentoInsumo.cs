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
        DirecaoMovimentoInsumo direcao,
        decimal quantidadeOriginal,
        UnidadeFisicaEstoque? unidadeOriginal,
        Guid? formaCompraId,
        string? formaCompraNome,
        QuantidadeEstoque? fatorCompra,
        QuantidadeEstoque quantidadeBase,
        QuantidadeEstoque? quantidadeContada,
        QuantidadeEstoque saldoAnterior,
        QuantidadeEstoque saldoPosterior,
        CustoAquisicaoInsumo? custo,
        Guid? movimentoOriginalId,
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
        Direcao = direcao;
        QuantidadeOriginal = quantidadeOriginal;
        UnidadeOriginal = unidadeOriginal;
        FormaCompraId = formaCompraId;
        FormaCompraNome = formaCompraNome;
        FatorCompra = fatorCompra;
        QuantidadeBase = quantidadeBase;
        QuantidadeContada = quantidadeContada;
        SaldoAnterior = saldoAnterior;
        SaldoPosterior = saldoPosterior;
        CustoTotal = custo?.Total;
        CustoPorUnidadeBase = custo?.PorUnidadeBase;
        MovimentoOriginalId = movimentoOriginalId;
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
    public DirecaoMovimentoInsumo Direcao { get; private set; }
    public decimal QuantidadeOriginal { get; private set; }
    public UnidadeFisicaEstoque? UnidadeOriginal { get; private set; }
    public Guid? FormaCompraId { get; private set; }
    public string? FormaCompraNome { get; private set; }
    public QuantidadeEstoque? FatorCompra { get; private set; }
    public QuantidadeEstoque QuantidadeBase { get; private set; } = null!;
    public QuantidadeEstoque? QuantidadeContada { get; private set; }
    public QuantidadeEstoque SaldoAnterior { get; private set; } = null!;
    public QuantidadeEstoque SaldoPosterior { get; private set; } = null!;
    public decimal? CustoTotal { get; private set; }
    public decimal? CustoPorUnidadeBase { get; private set; }
    public Guid? MovimentoOriginalId { get; private set; }
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
        if (tipo is not (TipoMovimentoInsumo.EstoqueInicial or TipoMovimentoInsumo.Aquisicao))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), "O tipo não representa uma entrada.");
        }

        ArgumentNullException.ThrowIfNull(custo);
        var motivo = tipo == TipoMovimentoInsumo.EstoqueInicial ? "Estoque inicial" : "Aquisição";
        return Criar(
            insumo,
            lote,
            responsavelId,
            tipo,
            DirecaoMovimentoInsumo.Entrada,
            quantidadeOriginal,
            unidadeOriginal,
            conversaoCompra?.FormaCompraId,
            conversaoCompra?.NomeFormaCompra,
            conversaoCompra?.FatorAplicado,
            quantidadeBase,
            quantidadeContada: null,
            saldoAnterior,
            custo,
            movimentoOriginalId: null,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
    }

    internal static MovimentoInsumo CriarSaida(
        InsumoEstoque insumo,
        LoteInsumo? lote,
        Guid responsavelId,
        TipoMovimentoInsumo tipo,
        decimal quantidadeOriginal,
        UnidadeFisicaEstoque unidadeOriginal,
        QuantidadeEstoque quantidadeBase,
        QuantidadeEstoque saldoAnterior,
        string motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao)
    {
        if (tipo is not (TipoMovimentoInsumo.SaidaManual
            or TipoMovimentoInsumo.Perda
            or TipoMovimentoInsumo.Descarte))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), "O tipo não representa uma saída.");
        }

        return Criar(
            insumo,
            lote,
            responsavelId,
            tipo,
            DirecaoMovimentoInsumo.Saida,
            quantidadeOriginal,
            unidadeOriginal,
            formaCompraId: null,
            formaCompraNome: null,
            fatorCompra: null,
            quantidadeBase,
            quantidadeContada: null,
            saldoAnterior,
            custo: null,
            movimentoOriginalId: null,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
    }

    internal static MovimentoInsumo CriarAjuste(
        InsumoEstoque insumo,
        LoteInsumo? lote,
        Guid responsavelId,
        decimal quantidadeContadaOriginal,
        UnidadeFisicaEstoque unidadeOriginal,
        QuantidadeEstoque quantidadeContada,
        QuantidadeEstoque quantidadeDiferenca,
        QuantidadeEstoque saldoAnterior,
        string motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao)
    {
        quantidadeDiferenca.ExigirPositiva(nameof(quantidadeDiferenca));
        if (quantidadeContada.ValorBase == saldoAnterior.ValorBase)
        {
            throw new ArgumentException("Uma contagem sem diferença não gera movimento.", nameof(quantidadeDiferenca));
        }

        var direcao = quantidadeContada.ValorBase > saldoAnterior.ValorBase
            ? DirecaoMovimentoInsumo.Entrada
            : DirecaoMovimentoInsumo.Saida;
        return Criar(
            insumo,
            lote,
            responsavelId,
            TipoMovimentoInsumo.AjusteInventario,
            direcao,
            quantidadeContadaOriginal,
            unidadeOriginal,
            formaCompraId: null,
            formaCompraNome: null,
            fatorCompra: null,
            quantidadeDiferenca,
            quantidadeContada,
            saldoAnterior,
            custo: null,
            movimentoOriginalId: null,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
    }

    internal static MovimentoInsumo CriarEstorno(
        InsumoEstoque insumo,
        LoteInsumo? lote,
        Guid responsavelId,
        MovimentoInsumo original,
        QuantidadeEstoque saldoAnterior,
        string motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Tipo == TipoMovimentoInsumo.Estorno
            || original.InsumoId != insumo.Id
            || original.LoteId != lote?.Id)
        {
            throw new ArgumentException("Somente movimento original da mesma posição pode ser estornado.", nameof(original));
        }

        return Criar(
            insumo,
            lote,
            responsavelId,
            TipoMovimentoInsumo.Estorno,
            original.Direcao == DirecaoMovimentoInsumo.Entrada
                ? DirecaoMovimentoInsumo.Saida
                : DirecaoMovimentoInsumo.Entrada,
            original.QuantidadeBase.ValorBase,
            original.QuantidadeBase.UnidadeBase,
            formaCompraId: null,
            formaCompraNome: null,
            fatorCompra: null,
            QuantidadeEstoque.Criar(
                original.QuantidadeBase.ValorBase,
                original.QuantidadeBase.UnidadeBase),
            quantidadeContada: null,
            saldoAnterior,
            custo: null,
            original.Id,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
    }

    private static MovimentoInsumo Criar(
        InsumoEstoque insumo,
        LoteInsumo? lote,
        Guid responsavelId,
        TipoMovimentoInsumo tipo,
        DirecaoMovimentoInsumo direcao,
        decimal quantidadeOriginal,
        UnidadeFisicaEstoque? unidadeOriginal,
        Guid? formaCompraId,
        string? formaCompraNome,
        QuantidadeEstoque? fatorCompra,
        QuantidadeEstoque quantidadeBase,
        QuantidadeEstoque? quantidadeContada,
        QuantidadeEstoque saldoAnterior,
        CustoAquisicaoInsumo? custo,
        Guid? movimentoOriginalId,
        string motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao)
    {
        ArgumentNullException.ThrowIfNull(insumo);
        ArgumentNullException.ThrowIfNull(quantidadeBase);
        ArgumentNullException.ThrowIfNull(saldoAnterior);
        ArgumentNullException.ThrowIfNull(fingerprintRequisicao);

        if (insumo.Id == Guid.Empty || responsavelId == Guid.Empty)
        {
            throw new ArgumentException("Insumo e responsável devem ter identificadores válidos.");
        }

        var tipoAceitaDirecao = tipo switch
        {
            TipoMovimentoInsumo.EstoqueInicial or TipoMovimentoInsumo.Aquisicao =>
                direcao == DirecaoMovimentoInsumo.Entrada,
            TipoMovimentoInsumo.SaidaManual or TipoMovimentoInsumo.Perda or TipoMovimentoInsumo.Descarte =>
                direcao == DirecaoMovimentoInsumo.Saida,
            TipoMovimentoInsumo.AjusteInventario or TipoMovimentoInsumo.Estorno =>
                Enum.IsDefined(direcao),
            _ => false
        };
        if (!Enum.IsDefined(tipo) || !Enum.IsDefined(direcao) || !tipoAceitaDirecao)
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), "Tipo ou direção de movimento inválido.");
        }

        quantidadeBase.ExigirPositiva(nameof(quantidadeBase));
        if (quantidadeBase.UnidadeBase != insumo.UnidadeBase
            || saldoAnterior.UnidadeBase != insumo.UnidadeBase
            || quantidadeContada is not null && quantidadeContada.UnidadeBase != insumo.UnidadeBase)
        {
            throw new ArgumentException("As quantidades devem estar na unidade-base do insumo.");
        }

        if (insumo.ControlaLote != (lote is not null)
            || lote is not null && lote.InsumoId != insumo.Id)
        {
            throw new ArgumentException("O lote deve corresponder à configuração e ao insumo.", nameof(lote));
        }

        if (quantidadeOriginal < 0m
            || quantidadeOriginal == 0m && tipo != TipoMovimentoInsumo.AjusteInventario)
        {
            throw new ArgumentOutOfRangeException(nameof(quantidadeOriginal), "A quantidade original é inválida.");
        }

        if (registradoEmUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante de registro deve estar em UTC.", nameof(registradoEmUtc));
        }

        var key = NormalizarObrigatorio(
            chaveIdempotencia,
            ChaveIdempotenciaMaxLength,
            nameof(chaveIdempotencia));
        var normalizedReason = NormalizarObrigatorio(motivo, MotivoMaxLength, nameof(motivo));
        var normalizedNote = string.IsNullOrWhiteSpace(observacao)
            ? null
            : NormalizarObrigatorio(observacao, ObservacaoMaxLength, nameof(observacao));

        decimal saldoPosteriorValor;
        try
        {
            var delta = direcao == DirecaoMovimentoInsumo.Entrada
                ? quantidadeBase.ValorBase
                : -quantidadeBase.ValorBase;
            saldoPosteriorValor = checked(saldoAnterior.ValorBase + delta);
            if (saldoPosteriorValor < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantidadeBase),
                    "A operação deixaria o saldo físico negativo.");
            }
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidadeBase),
                "A operação excede o limite de quantidade-base.");
        }

        if (fingerprintRequisicao.Length != 32)
        {
            throw new ArgumentException("O fingerprint deve conter 32 bytes.", nameof(fingerprintRequisicao));
        }

        var saldoPosterior = QuantidadeEstoque.Criar(saldoPosteriorValor, insumo.UnidadeBase);
        return new MovimentoInsumo(
            Guid.NewGuid(),
            insumo.Id,
            lote?.Id,
            responsavelId,
            tipo,
            direcao,
            quantidadeOriginal,
            unidadeOriginal,
            formaCompraId,
            formaCompraNome,
            fatorCompra,
            quantidadeBase,
            quantidadeContada,
            saldoAnterior,
            saldoPosterior,
            custo,
            movimentoOriginalId,
            normalizedReason,
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
