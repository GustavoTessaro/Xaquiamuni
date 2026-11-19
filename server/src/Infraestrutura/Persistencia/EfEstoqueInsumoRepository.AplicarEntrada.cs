using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueInsumoRepository
{
    // O chamador controla a transação, os locks e a gravação.
    private async Task<RegistrarEntradaInsumoResult> AplicarEntradaAsync(
        RegistrarEntradaInsumoInput input, string chaveIdempotencia, byte[] fingerprint,
        CancellationToken cancellationToken, ComponenteCompraMateriaPrima? snapshot = null,
        DateOnly? dataOperacional = null, DateTimeOffset? agora = null, InsumoEstoque? insumoCarregado = null)
    {
        var insumo = insumoCarregado ?? await _dbContext.InsumosEstoque
            .Include(item => item.FormasCompra)
            .SingleOrDefaultAsync(item => item.Id == input.InsumoId, cancellationToken)
            .ConfigureAwait(false);
        if (insumo is null)
        {
            return RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.InsumoNaoEncontrado,
                "O insumo informado não existe.");
        }

        if (!insumo.Ativo)
        {
            return RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.InsumoInativo,
                "Insumos inativos não recebem novas entradas.");
        }

        var validacao = ValidarTipoQuantidadeEObservacao(input);
        if (validacao is not null)
        {
            return RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.EntradaInvalida,
                validacao);
        }

        var conversao = ResolverQuantidade(insumo, input, out var quantidadeBase, out var erroQuantidade);
        if (erroQuantidade is not null || quantidadeBase is null)
        {
            return RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.EntradaInvalida,
                erroQuantidade ?? "A quantidade informada é inválida.");
        }

        if (snapshot is not null)
        {
            if (snapshot.QuantidadeBase != quantidadeBase.ValorBase ||
                snapshot.UnidadeBase != quantidadeBase.UnidadeBase ||
                snapshot.Fator != conversao?.FatorAplicado.ValorBase)
            {
                return RegistrarEntradaInsumoResult.Failure(
                    RegistrarEntradaInsumoStatus.ConflitoConcorrencia,
                    "A conversão mudou. Revise o rascunho antes de receber a compra.");
            }

            conversao = snapshot.ReconstituirConversao();
        }

        LoteInsumo? lote = null;
        var loteCriado = false;
        if (!insumo.ControlaLote)
        {
            if (input.LoteId is not null
                || input.IdentificacaoNovoLote is not null
                || input.DataEntradaNovoLote is not null
                || input.DataValidadeNovoLote is not null
                || input.FornecedorNovoLote is not null
                || input.NovoLoteBloqueado)
            {
                return RegistrarEntradaInsumoResult.Failure(
                    RegistrarEntradaInsumoStatus.EntradaInvalida,
                "Insumos sem controle por lote não aceitam dados de lote.");
            }
        }
        else if (input.LoteId is Guid loteId)
        {
            if (input.IdentificacaoNovoLote is not null
                || input.DataEntradaNovoLote is not null
                || input.DataValidadeNovoLote is not null
                || input.FornecedorNovoLote is not null
                || input.NovoLoteBloqueado)
            {
                return RegistrarEntradaInsumoResult.Failure(
                    RegistrarEntradaInsumoStatus.EntradaInvalida,
                "Informe um lote existente ou os dados de um novo lote, não ambos.");
            }

            lote = await _dbContext.LotesInsumo.SingleOrDefaultAsync(
                    item => item.Id == loteId && item.InsumoId == insumo.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            if (lote is null)
            {
                return RegistrarEntradaInsumoResult.Failure(
                    RegistrarEntradaInsumoStatus.LoteNaoEncontrado,
                "O lote não existe para este insumo.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.IdentificacaoNovoLote)
                || input.DataEntradaNovoLote is null)
            {
                return RegistrarEntradaInsumoResult.Failure(
                    RegistrarEntradaInsumoStatus.EntradaInvalida,
                "Um novo lote exige identificação e data de entrada.");
            }

            try
            {
                lote = LoteInsumo.Criar(
                    insumo,
                    input.IdentificacaoNovoLote,
                    input.DataEntradaNovoLote.Value,
                    input.DataValidadeNovoLote,
                    input.FornecedorNovoLote,
                    input.NovoLoteBloqueado);
                loteCriado = true;
            }
            catch (ArgumentException exception)
            {
                return RegistrarEntradaInsumoResult.Failure(
                    RegistrarEntradaInsumoStatus.EntradaInvalida,
                exception.Message);
            }
        }

        if (input.Tipo == TipoMovimentoInsumo.Aquisicao
            && lote is not null
            && !lote.EstaElegivelEm(dataOperacional ?? _calendario.ObterDataAtual()))
        {
            return RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.LoteInelegivel,
                "Aquisições ordinárias exigem lote elegível na data de registro.");
        }

        var custo = CustoAquisicaoInsumo.Criar(input.CustoTotal, quantidadeBase);
        var loteIdSelecionado = lote?.Id;
        var saldo = await _dbContext.SaldosInsumo.SingleOrDefaultAsync(
                item => item.InsumoId == insumo.Id && item.LoteId == loteIdSelecionado,
                cancellationToken)
            .ConfigureAwait(false);
        if (saldo is null)
        {
            saldo = SaldoInsumo.CriarZero(insumo, lote);
            _dbContext.SaldosInsumo.Add(saldo);
        }

        if (input.Tipo == TipoMovimentoInsumo.EstoqueInicial)
        {
            var existeHistorico = await _dbContext.MovimentosInsumo.AnyAsync(
                movimento => movimento.InsumoId == insumo.Id
                    && movimento.LoteId == loteIdSelecionado,
                cancellationToken).ConfigureAwait(false);
            if (existeHistorico || saldo.QuantidadeFisica.ValorBase != 0m)
            {
                return RegistrarEntradaInsumoResult.Failure(
                    RegistrarEntradaInsumoStatus.EntradaInvalida,
                "O estoque inicial somente pode abrir uma posição sem saldo ou movimentos anteriores.");
            }
        }

        if (loteCriado)
        {
            _dbContext.LotesInsumo.Add(lote!);
        }

        var movement = MovimentoInsumo.CriarEntrada(
            insumo,
            lote,
            input.ResponsavelId,
            input.Tipo,
            input.QuantidadeOriginal,
            input.UnidadeOriginal,
            conversao,
            quantidadeBase,
            saldo.QuantidadeFisica,
            custo,
            input.Observacao,
            agora ?? _timeProvider.GetUtcNow(),
            chaveIdempotencia,
            fingerprint);
        insumo.AplicarMovimento(saldo, movement);
        _dbContext.MovimentosInsumo.Add(movement);

        return RegistrarEntradaInsumoResult.Success(movement);
    }
}
