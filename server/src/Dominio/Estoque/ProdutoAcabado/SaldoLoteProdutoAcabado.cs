using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class SaldoLoteProdutoAcabado
{
    private int _quantidadeVendavel;
    private int _quantidadeDanificada;
    private int _quantidadeQuarentena;

    private SaldoLoteProdutoAcabado(
        Guid id,
        Guid produtoVarianteId,
        Guid loteId,
        int quantidadeVendavel,
        int quantidadeDanificada,
        int quantidadeQuarentena)
    {
        Id = id;
        ProdutoVarianteId = produtoVarianteId;
        LoteId = loteId;
        _quantidadeVendavel = quantidadeVendavel;
        _quantidadeDanificada = quantidadeDanificada;
        _quantidadeQuarentena = quantidadeQuarentena;
    }

    private SaldoLoteProdutoAcabado()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public Guid LoteId { get; private set; }
    public int QuantidadeVendavel => _quantidadeVendavel;
    public int QuantidadeDanificada => _quantidadeDanificada;
    public int QuantidadeQuarentena => _quantidadeQuarentena;
    public int QuantidadeFisica => SomarQuantidades(
        _quantidadeVendavel,
        _quantidadeDanificada,
        _quantidadeQuarentena,
        nameof(QuantidadeFisica));

    public static SaldoLoteProdutoAcabado CriarZero(LoteProdutoAcabado? lote)
    {
        ArgumentNullException.ThrowIfNull(lote);
        if (lote.Id == Guid.Empty || lote.ProdutoVarianteId == Guid.Empty)
        {
            throw new ArgumentException("O lote deve possuir identificadores válidos.", nameof(lote));
        }

        return new SaldoLoteProdutoAcabado(
            Guid.NewGuid(),
            lote.ProdutoVarianteId,
            lote.Id,
            0,
            0,
            0);
    }

    public MovimentoProdutoAcabado RegistrarEntrada(
        LoteProdutoAcabado lote,
        int quantidade,
        CondicaoEstoqueProdutoAcabado condicaoDestino,
        TipoMovimentoProdutoAcabado tipo,
        Guid responsavelId,
        string? motivo,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        string? observacao = null) =>
        AplicarAlteracao(
            lote,
            tipo,
            quantidade,
            condicaoOrigem: null,
            condicaoDestino,
            quantidadeContada: null,
            movimentoOriginalId: null,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);

    public MovimentoProdutoAcabado RegistrarSaidaManual(
        ProdutoVariante? variante,
        LoteProdutoAcabado lote,
        int quantidade,
        DateOnly dataReferencia,
        Guid responsavelId,
        string? motivo,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        string? observacao = null)
    {
        ValidarSaidaVendavel(variante, lote, dataReferencia);

        return AplicarAlteracao(
            lote,
            TipoMovimentoProdutoAcabado.SaidaManual,
            quantidade,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            condicaoDestino: null,
            quantidadeContada: null,
            movimentoOriginalId: null,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
    }

    public MovimentoProdutoAcabado RegistrarRetirada(
        ReservaProdutoAcabado reserva,
        ProdutoVariante variante,
        LoteProdutoAcabado lote,
        DateOnly dataReferencia,
        Guid responsavelId,
        DateTimeOffset registradoEmUtc)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        if (reserva.Estado != EstadoReservaProdutoAcabado.Confirmada)
        {
            throw new InvalidOperationException("Somente uma reserva confirmada pode ser retirada.");
        }

        ValidarSaidaVendavel(variante, lote, dataReferencia);
        var movimento = AplicarAlteracao(
            lote, TipoMovimentoProdutoAcabado.SaidaManual, reserva.Quantidade,
            CondicaoEstoqueProdutoAcabado.Vendavel, condicaoDestino: null,
            quantidadeContada: null, movimentoOriginalId: null,
            responsavelId, "Retirada de pedido", observacao: null, registradoEmUtc,
            chaveIdempotencia: $"retirada:{Guid.NewGuid():N}",
            fingerprintRequisicao: System.Security.Cryptography.SHA256.HashData(reserva.Id.ToByteArray()),
            reservaProdutoAcabadoId: reserva.Id, aplicar: false);

        reserva.Consumir(variante, lote, this, dataReferencia, registradoEmUtc);
        AplicarMovimento(movimento);
        return movimento;
    }

    private void ValidarSaidaVendavel(ProdutoVariante? variante, LoteProdutoAcabado lote, DateOnly dataReferencia)
    {
        ValidarVarianteDoLote(variante, lote);
        if (!variante!.Ativa)
        {
            throw new InvalidOperationException("Uma variante inativa não pode ter saída vendável.");
        }

        if (!lote.EstaValidoEm(dataReferencia))
        {
            throw new InvalidOperationException("Um lote vencido não pode ter saída vendável.");
        }
    }

    public MovimentoProdutoAcabado RegistrarPerda(
        LoteProdutoAcabado lote,
        int quantidade,
        CondicaoEstoqueProdutoAcabado condicaoOrigem,
        Guid responsavelId,
        string? motivo,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        string? observacao = null) =>
        AplicarAlteracao(
            lote,
            TipoMovimentoProdutoAcabado.Perda,
            quantidade,
            condicaoOrigem,
            condicaoDestino: null,
            quantidadeContada: null,
            movimentoOriginalId: null,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);

    public MovimentoProdutoAcabado RegistrarDescarte(
        LoteProdutoAcabado lote,
        int quantidade,
        CondicaoEstoqueProdutoAcabado condicaoOrigem,
        Guid responsavelId,
        string? motivo,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        string? observacao = null) =>
        AplicarAlteracao(
            lote,
            TipoMovimentoProdutoAcabado.Descarte,
            quantidade,
            condicaoOrigem,
            condicaoDestino: null,
            quantidadeContada: null,
            movimentoOriginalId: null,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);

    public MovimentoProdutoAcabado TransferirCondicao(
        LoteProdutoAcabado lote,
        int quantidade,
        CondicaoEstoqueProdutoAcabado condicaoOrigem,
        CondicaoEstoqueProdutoAcabado condicaoDestino,
        Guid responsavelId,
        string? motivo,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        string? observacao = null) =>
        AplicarAlteracao(
            lote,
            TipoMovimentoProdutoAcabado.TransferenciaCondicao,
            quantidade,
            condicaoOrigem,
            condicaoDestino,
            quantidadeContada: null,
            movimentoOriginalId: null,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);

    public MovimentoProdutoAcabado? Contar(
        LoteProdutoAcabado lote,
        CondicaoEstoqueProdutoAcabado condicao,
        int quantidadeEncontrada,
        Guid responsavelId,
        string? motivo,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        string? observacao = null)
    {
        ValidarContexto(
            lote,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
        ValidarCondicao(condicao, nameof(condicao));
        if (quantidadeEncontrada < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidadeEncontrada),
                "A quantidade contada não pode ser negativa.");
        }

        var quantidadeAtual = ObterQuantidade(condicao);
        if (quantidadeEncontrada == quantidadeAtual)
        {
            return null;
        }

        var quantidadeDiferenca = quantidadeEncontrada > quantidadeAtual
            ? quantidadeEncontrada - quantidadeAtual
            : quantidadeAtual - quantidadeEncontrada;
        CondicaoEstoqueProdutoAcabado? condicaoOrigem =
            quantidadeEncontrada < quantidadeAtual ? condicao : null;
        CondicaoEstoqueProdutoAcabado? condicaoDestino =
            quantidadeEncontrada > quantidadeAtual ? condicao : null;

        return AplicarAlteracao(
            lote,
            TipoMovimentoProdutoAcabado.AjusteInventario,
            quantidadeDiferenca,
            condicaoOrigem,
            condicaoDestino,
            quantidadeEncontrada,
            movimentoOriginalId: null,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
    }

    public MovimentoProdutoAcabado Estornar(
        LoteProdutoAcabado lote,
        MovimentoProdutoAcabado? movimentoOriginal,
        IEnumerable<MovimentoProdutoAcabado> historico,
        Guid responsavelId,
        string? motivo,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        string? observacao = null)
    {
        ValidarContexto(
            lote,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
        ArgumentNullException.ThrowIfNull(movimentoOriginal);
        ArgumentNullException.ThrowIfNull(historico);
        if (movimentoOriginal.Tipo == TipoMovimentoProdutoAcabado.Estorno
            || movimentoOriginal.ReservaProdutoAcabadoId is not null
            || movimentoOriginal.ProdutoVarianteId != ProdutoVarianteId
            || movimentoOriginal.LoteId != LoteId
            || lote.Id != LoteId
            || lote.ProdutoVarianteId != ProdutoVarianteId)
        {
            throw new ArgumentException(
                "Somente um movimento original desta posição pode ser estornado.",
                nameof(movimentoOriginal));
        }

        var movimentos = historico.ToArray();
        if (!movimentos.Any(movimento => movimento.Id == movimentoOriginal.Id))
        {
            throw new ArgumentException(
                "O movimento original deve pertencer ao histórico informado.",
                nameof(historico));
        }

        if (movimentos.Any(movimento =>
                movimento.MovimentoOriginalId == movimentoOriginal.Id))
        {
            throw new InvalidOperationException("O movimento original já foi estornado.");
        }

        return AplicarAlteracao(
            lote,
            TipoMovimentoProdutoAcabado.Estorno,
            movimentoOriginal.Quantidade,
            movimentoOriginal.CondicaoDestino,
            movimentoOriginal.CondicaoOrigem,
            quantidadeContada: null,
            movimentoOriginal.Id,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
    }

    internal void AplicarMovimento(MovimentoProdutoAcabado movimento)
    {
        ArgumentNullException.ThrowIfNull(movimento);
        if (movimento.ProdutoVarianteId != ProdutoVarianteId
            || movimento.LoteId != LoteId
            || movimento.SaldoAnterior != CriarSnapshot())
        {
            throw new InvalidOperationException(
                "O movimento não corresponde ao saldo atual deste lote.");
        }

        _quantidadeVendavel = movimento.SaldoPosterior.QuantidadeVendavel;
        _quantidadeDanificada = movimento.SaldoPosterior.QuantidadeDanificada;
        _quantidadeQuarentena = movimento.SaldoPosterior.QuantidadeQuarentena;
    }

    private MovimentoProdutoAcabado AplicarAlteracao(
        LoteProdutoAcabado lote,
        TipoMovimentoProdutoAcabado tipo,
        int quantidade,
        CondicaoEstoqueProdutoAcabado? condicaoOrigem,
        CondicaoEstoqueProdutoAcabado? condicaoDestino,
        int? quantidadeContada,
        Guid? movimentoOriginalId,
        Guid responsavelId,
        string? motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao,
        Guid? reservaProdutoAcabadoId = null,
        bool aplicar = true)
    {
        ValidarContexto(
            lote,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao);
        if (quantidade <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantidade), "A quantidade deve ser positiva.");
        }

        ValidarCondicaoOpcional(condicaoOrigem, nameof(condicaoOrigem));
        ValidarCondicaoOpcional(condicaoDestino, nameof(condicaoDestino));
        if (condicaoOrigem == condicaoDestino
            || condicaoOrigem is null && condicaoDestino is null)
        {
            throw new ArgumentException("A operação deve alterar ao menos uma condição.");
        }

        var saldoAnterior = CriarSnapshot();
        var quantidades = new Dictionary<CondicaoEstoqueProdutoAcabado, int>
        {
            [CondicaoEstoqueProdutoAcabado.Vendavel] = _quantidadeVendavel,
            [CondicaoEstoqueProdutoAcabado.Danificada] = _quantidadeDanificada,
            [CondicaoEstoqueProdutoAcabado.Quarentena] = _quantidadeQuarentena
        };

        if (condicaoOrigem is not null)
        {
            var quantidadeOrigem = quantidades[condicaoOrigem.Value];
            if (quantidade > quantidadeOrigem)
            {
                throw new InvalidOperationException(
                    "A quantidade excede o saldo da condição de origem.");
            }

            quantidades[condicaoOrigem.Value] = quantidadeOrigem - quantidade;
        }

        if (condicaoDestino is not null)
        {
            try
            {
                quantidades[condicaoDestino.Value] =
                    checked(quantidades[condicaoDestino.Value] + quantidade);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantidade),
                    "A operação excede o limite de quantidade inteira suportado.");
            }
        }

        var saldoPosterior = SaldoProdutoAcabadoSnapshot.Criar(
            quantidades[CondicaoEstoqueProdutoAcabado.Vendavel],
            quantidades[CondicaoEstoqueProdutoAcabado.Danificada],
            quantidades[CondicaoEstoqueProdutoAcabado.Quarentena]);
        var movimento = MovimentoProdutoAcabado.Criar(
            ProdutoVarianteId,
            LoteId,
            tipo,
            quantidade,
            condicaoOrigem,
            condicaoDestino,
            quantidadeContada,
            saldoAnterior,
            saldoPosterior,
            movimentoOriginalId,
            responsavelId,
            motivo,
            observacao,
            registradoEmUtc,
            chaveIdempotencia,
            fingerprintRequisicao,
            reservaProdutoAcabadoId);
        if (aplicar)
        {
            AplicarMovimento(movimento);
        }
        return movimento;
    }

    private void ValidarContexto(
        LoteProdutoAcabado? lote,
        Guid responsavelId,
        string? motivo,
        string? observacao,
        DateTimeOffset registradoEmUtc,
        string? chaveIdempotencia,
        byte[]? fingerprintRequisicao)
    {
        ValidarLote(lote);
        _ = MovimentoProdutoAcabado.NormalizarMotivo(motivo);
        if (responsavelId == Guid.Empty)
        {
            throw new ArgumentException("O responsável deve possuir um identificador válido.", nameof(responsavelId));
        }

        if (registradoEmUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante de registro deve estar em UTC.", nameof(registradoEmUtc));
        }

        _ = MovimentoProdutoAcabado.NormalizarChave(chaveIdempotencia);
        MovimentoProdutoAcabado.ValidarFingerprint(fingerprintRequisicao);
        _ = MovimentoProdutoAcabado.NormalizarObservacao(observacao);
    }

    private void ValidarLote(LoteProdutoAcabado? lote)
    {
        ArgumentNullException.ThrowIfNull(lote);
        if (lote.Id != LoteId || lote.ProdutoVarianteId != ProdutoVarianteId)
        {
            throw new ArgumentException(
                "O lote não corresponde a esta posição de estoque.",
                nameof(lote));
        }
    }

    private void ValidarVarianteDoLote(ProdutoVariante? variante, LoteProdutoAcabado lote)
    {
        ArgumentNullException.ThrowIfNull(variante);
        ValidarLote(lote);
        if (variante.Id != ProdutoVarianteId)
        {
            throw new ArgumentException(
                "A variante não corresponde à posição de estoque.",
                nameof(variante));
        }
    }

    private SaldoProdutoAcabadoSnapshot CriarSnapshot() =>
        SaldoProdutoAcabadoSnapshot.Criar(
            _quantidadeVendavel,
            _quantidadeDanificada,
            _quantidadeQuarentena);

    private int ObterQuantidade(CondicaoEstoqueProdutoAcabado condicao) =>
        condicao switch
        {
            CondicaoEstoqueProdutoAcabado.Vendavel => _quantidadeVendavel,
            CondicaoEstoqueProdutoAcabado.Danificada => _quantidadeDanificada,
            CondicaoEstoqueProdutoAcabado.Quarentena => _quantidadeQuarentena,
            _ => throw new ArgumentOutOfRangeException(nameof(condicao), condicao, "Condição de estoque inválida.")
        };

    private static int SomarQuantidades(
        int vendavel,
        int danificada,
        int quarentena,
        string paramName)
    {
        try
        {
            return checked(checked(vendavel + danificada) + quarentena);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "A soma das condições excede o limite de quantidade inteira suportado.");
        }
    }

    private static void ValidarCondicao(CondicaoEstoqueProdutoAcabado condicao, string paramName)
    {
        if (!Enum.IsDefined(condicao))
        {
            throw new ArgumentOutOfRangeException(paramName, condicao, "Condição de estoque inválida.");
        }
    }

    private static void ValidarCondicaoOpcional(
        CondicaoEstoqueProdutoAcabado? condicao,
        string paramName)
    {
        if (condicao is not null)
        {
            ValidarCondicao(condicao.Value, paramName);
        }
    }
}
