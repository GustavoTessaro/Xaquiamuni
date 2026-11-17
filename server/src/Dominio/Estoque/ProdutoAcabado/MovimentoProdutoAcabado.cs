namespace Xaquiamuni.Dominio.Estoque;

public sealed class MovimentoProdutoAcabado
{
    public const int ChaveIdempotenciaMaxLength = 128;
    public const int MotivoMaxLength = 120;
    public const int ObservacaoMaxLength = 500;

    private readonly byte[] _fingerprintRequisicao;

    private MovimentoProdutoAcabado(
        Guid id,
        Guid produtoVarianteId,
        Guid loteId,
        TipoMovimentoProdutoAcabado tipo,
        int quantidade,
        CondicaoEstoqueProdutoAcabado? condicaoOrigem,
        CondicaoEstoqueProdutoAcabado? condicaoDestino,
        int? quantidadeContada,
        SaldoProdutoAcabadoSnapshot saldoAnterior,
        SaldoProdutoAcabadoSnapshot saldoPosterior,
        Guid? movimentoOriginalId,
        Guid responsavelId,
        string motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string chaveIdempotencia,
        byte[] fingerprintRequisicao)
    {
        Id = id;
        ProdutoVarianteId = produtoVarianteId;
        LoteId = loteId;
        Tipo = tipo;
        Quantidade = quantidade;
        CondicaoOrigem = condicaoOrigem;
        CondicaoDestino = condicaoDestino;
        QuantidadeContada = quantidadeContada;
        SaldoAnterior = saldoAnterior;
        SaldoPosterior = saldoPosterior;
        MovimentoOriginalId = movimentoOriginalId;
        ResponsavelId = responsavelId;
        Motivo = motivo;
        Observacao = observacao;
        RegistradoEmUtc = registradoEmUtc;
        ChaveIdempotencia = chaveIdempotencia;
        _fingerprintRequisicao = fingerprintRequisicao.ToArray();
    }

    private MovimentoProdutoAcabado()
    {
        _fingerprintRequisicao = [];
    }

    public Guid Id { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public Guid LoteId { get; private set; }
    public TipoMovimentoProdutoAcabado Tipo { get; private set; }
    public int Quantidade { get; private set; }
    public CondicaoEstoqueProdutoAcabado? CondicaoOrigem { get; private set; }
    public CondicaoEstoqueProdutoAcabado? CondicaoDestino { get; private set; }
    public int? QuantidadeContada { get; private set; }
    public SaldoProdutoAcabadoSnapshot SaldoAnterior { get; private set; } = null!;
    public SaldoProdutoAcabadoSnapshot SaldoPosterior { get; private set; } = null!;
    public Guid? MovimentoOriginalId { get; private set; }
    public Guid ResponsavelId { get; private set; }
    public string Motivo { get; private set; } = null!;
    public string? Observacao { get; private set; }
    public DateTimeOffset RegistradoEmUtc { get; private set; }
    public string ChaveIdempotencia { get; private set; } = null!;
    public byte[] FingerprintRequisicao => _fingerprintRequisicao.ToArray();

    internal static MovimentoProdutoAcabado Criar(
        Guid produtoVarianteId,
        Guid loteId,
        TipoMovimentoProdutoAcabado tipo,
        int quantidade,
        CondicaoEstoqueProdutoAcabado? condicaoOrigem,
        CondicaoEstoqueProdutoAcabado? condicaoDestino,
        int? quantidadeContada,
        SaldoProdutoAcabadoSnapshot saldoAnterior,
        SaldoProdutoAcabadoSnapshot saldoPosterior,
        Guid? movimentoOriginalId,
        Guid responsavelId,
        string? motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao)
    {
        ArgumentNullException.ThrowIfNull(saldoAnterior);
        ArgumentNullException.ThrowIfNull(saldoPosterior);
        if (produtoVarianteId == Guid.Empty || loteId == Guid.Empty || responsavelId == Guid.Empty)
        {
            throw new ArgumentException("Variante, lote e responsável devem possuir identificadores válidos.");
        }

        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de movimento inválido.");
        }

        if (quantidade <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantidade), "A quantidade deve ser positiva.");
        }

        ValidarCondicao(condicaoOrigem, nameof(condicaoOrigem));
        ValidarCondicao(condicaoDestino, nameof(condicaoDestino));
        ValidarTipoECondicoes(tipo, condicaoOrigem, condicaoDestino, quantidadeContada, movimentoOriginalId);
        if (registradoEmUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante de registro deve estar em UTC.", nameof(registradoEmUtc));
        }

        ValidarFingerprint(fingerprintRequisicao);
        return new MovimentoProdutoAcabado(
            Guid.NewGuid(),
            produtoVarianteId,
            loteId,
            tipo,
            quantidade,
            condicaoOrigem,
            condicaoDestino,
            quantidadeContada,
            saldoAnterior,
            saldoPosterior,
            movimentoOriginalId,
            responsavelId,
            NormalizarMotivo(motivo),
            NormalizarObservacao(observacao),
            registradoEmUtc,
            NormalizarChave(chaveIdempotencia),
            fingerprintRequisicao!);
    }

    internal static string NormalizarMotivo(string? motivo)
    {
        var normalized = NormalizarTextoObrigatorio(motivo, MotivoMaxLength, nameof(motivo));
        return normalized;
    }

    internal static string NormalizarChave(string? chaveIdempotencia) =>
        NormalizarTextoObrigatorio(
            chaveIdempotencia,
            ChaveIdempotenciaMaxLength,
            nameof(chaveIdempotencia));

    internal static string? NormalizarObservacao(string? observacao) =>
        string.IsNullOrWhiteSpace(observacao)
            ? null
            : NormalizarTextoObrigatorio(observacao, ObservacaoMaxLength, nameof(observacao));

    internal static void ValidarFingerprint(byte[]? fingerprintRequisicao)
    {
        if (fingerprintRequisicao is null || fingerprintRequisicao.Length != 32)
        {
            throw new ArgumentException(
                "O fingerprint da requisição deve conter 32 bytes.",
                nameof(fingerprintRequisicao));
        }
    }

    private static void ValidarTipoECondicoes(
        TipoMovimentoProdutoAcabado tipo,
        CondicaoEstoqueProdutoAcabado? condicaoOrigem,
        CondicaoEstoqueProdutoAcabado? condicaoDestino,
        int? quantidadeContada,
        Guid? movimentoOriginalId)
    {
        var condicoesValidas = (condicaoOrigem is null) != (condicaoDestino is null)
            || condicaoOrigem is not null
                && condicaoDestino is not null
                && condicaoOrigem != condicaoDestino;
        if (!condicoesValidas)
        {
            throw new ArgumentException("A movimentação deve alterar condições distintas.");
        }

        var tipoValido = tipo switch
        {
            TipoMovimentoProdutoAcabado.EstoqueInicial
                or TipoMovimentoProdutoAcabado.EntradaManual =>
                condicaoOrigem is null && condicaoDestino is not null
                    && quantidadeContada is null && movimentoOriginalId is null,
            TipoMovimentoProdutoAcabado.SaidaManual
                or TipoMovimentoProdutoAcabado.Perda
                or TipoMovimentoProdutoAcabado.Descarte =>
                condicaoOrigem is not null && condicaoDestino is null
                    && quantidadeContada is null && movimentoOriginalId is null,
            TipoMovimentoProdutoAcabado.TransferenciaCondicao =>
                condicaoOrigem is not null && condicaoDestino is not null
                    && quantidadeContada is null && movimentoOriginalId is null,
            TipoMovimentoProdutoAcabado.AjusteInventario =>
                (condicaoOrigem is null || condicaoDestino is null)
                    && quantidadeContada is >= 0 && movimentoOriginalId is null,
            TipoMovimentoProdutoAcabado.Estorno =>
                movimentoOriginalId is not null && movimentoOriginalId != Guid.Empty
                    && quantidadeContada is null,
            _ => false
        };

        if (!tipoValido)
        {
            throw new ArgumentException(
                "Tipo, condições e dados complementares do movimento são incompatíveis.",
                nameof(tipo));
        }
    }

    private static void ValidarCondicao(
        CondicaoEstoqueProdutoAcabado? condicao,
        string paramName)
    {
        if (condicao is not null && !Enum.IsDefined(condicao.Value))
        {
            throw new ArgumentOutOfRangeException(paramName, condicao, "Condição de estoque inválida.");
        }
    }

    private static string NormalizarTextoObrigatorio(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O valor é obrigatório.", paramName);
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"O valor não pode exceder {maxLength} caracteres.",
                paramName);
        }

        return normalized;
    }
}
