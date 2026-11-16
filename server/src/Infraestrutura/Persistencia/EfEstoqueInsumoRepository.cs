using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfEstoqueInsumoRepository : IEstoqueInsumoRepository
{
    private readonly XaquiamuniDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EfEstoqueInsumoRepository(
        XaquiamuniDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<RegistrarEntradaInsumoResult> RegistrarEntradaAsync(
        RegistrarEntradaInsumoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chaveIdempotencia = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        if (input.InsumoId == Guid.Empty || input.ResponsavelId == Guid.Empty)
        {
            return RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.EntradaInvalida,
                "Insumo e responsável são obrigatórios.");
        }

        if (chaveIdempotencia is null)
        {
            return RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.EntradaInvalida,
                "A chave de idempotência é obrigatória e deve ter até 128 caracteres.");
        }

        var fingerprint = CalcularFingerprint(input);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"stock-entry:{input.ResponsavelId:D}:{chaveIdempotencia}",
                cancellationToken).ConfigureAwait(false);

            var existente = await _dbContext.MovimentosInsumo
                .SingleOrDefaultAsync(
                    movimento => movimento.ResponsavelId == input.ResponsavelId
                        && movimento.ChaveIdempotencia == chaveIdempotencia,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existente is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        existente.FingerprintRequisicao,
                        fingerprint))
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    _dbContext.ChangeTracker.Clear();
                    return RegistrarEntradaInsumoResult.Failure(
                        RegistrarEntradaInsumoStatus.ConflitoIdempotencia,
                        "A chave de idempotência já foi usada com outros dados.");
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return RegistrarEntradaInsumoResult.Success(existente);
            }

            if (await _dbContext.AjustesSemDiferencaInsumo.AnyAsync(
                    item => item.ResponsavelId == input.ResponsavelId
                        && item.ChaveIdempotencia == chaveIdempotencia,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaInsumoStatus.ConflitoIdempotencia,
                    "A chave de idempotência já foi usada por outra operação.")
                    .ConfigureAwait(false);
            }

            await AdquirirLockAsync($"stock-supply:{input.InsumoId:D}", cancellationToken)
                .ConfigureAwait(false);
            var insumo = await _dbContext.InsumosEstoque
                .Include(item => item.FormasCompra)
                .SingleOrDefaultAsync(item => item.Id == input.InsumoId, cancellationToken)
                .ConfigureAwait(false);
            if (insumo is null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaInsumoStatus.InsumoNaoEncontrado,
                    "O insumo informado não existe.").ConfigureAwait(false);
            }

            if (!insumo.Ativo)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaInsumoStatus.InsumoInativo,
                    "Insumos inativos não recebem novas entradas.").ConfigureAwait(false);
            }

            var validacao = ValidarTipoQuantidadeEObservacao(input);
            if (validacao is not null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaInsumoStatus.EntradaInvalida,
                    validacao).ConfigureAwait(false);
            }

            var conversao = ResolverQuantidade(insumo, input, out var quantidadeBase, out var erroQuantidade);
            if (erroQuantidade is not null || quantidadeBase is null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaInsumoStatus.EntradaInvalida,
                    erroQuantidade ?? "A quantidade informada é inválida.").ConfigureAwait(false);
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
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaInsumoStatus.EntradaInvalida,
                        "Insumos sem controle por lote não aceitam dados de lote.").ConfigureAwait(false);
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
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaInsumoStatus.EntradaInvalida,
                        "Informe um lote existente ou os dados de um novo lote, não ambos.")
                        .ConfigureAwait(false);
                }

                lote = await _dbContext.LotesInsumo.SingleOrDefaultAsync(
                        item => item.Id == loteId && item.InsumoId == insumo.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (lote is null)
                {
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaInsumoStatus.LoteNaoEncontrado,
                        "O lote não existe para este insumo.").ConfigureAwait(false);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.IdentificacaoNovoLote)
                    || input.DataEntradaNovoLote is null)
                {
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaInsumoStatus.EntradaInvalida,
                        "Um novo lote exige identificação e data de entrada.").ConfigureAwait(false);
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
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaInsumoStatus.EntradaInvalida,
                        exception.Message).ConfigureAwait(false);
                }
            }

            if (input.Tipo == TipoMovimentoInsumo.Aquisicao
                && lote is not null
                && !lote.EstaElegivelEm(DateOnly.FromDateTime(
                    _timeProvider.GetUtcNow().UtcDateTime)))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaInsumoStatus.LoteInelegivel,
                    "Aquisições ordinárias exigem lote elegível na data de registro.").ConfigureAwait(false);
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
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaInsumoStatus.EntradaInvalida,
                        "O estoque inicial somente pode abrir uma posição sem saldo ou movimentos anteriores.")
                        .ConfigureAwait(false);
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
                _timeProvider.GetUtcNow(),
                chaveIdempotencia,
                fingerprint);
            insumo.AplicarMovimento(saldo, movement);
            _dbContext.MovimentosInsumo.Add(movement);

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return RegistrarEntradaInsumoResult.Success(movement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaInsumoStatus.ConflitoConcorrencia,
                "O estoque foi alterado concorrentemente; repita a operação com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaInsumoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaInsumoStatus.EntradaInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaInsumoStatus.EntradaInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<OperacaoEstoqueInsumoResult> RegistrarSaidaAsync(
        RegistrarSaidaInsumoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chave = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        var validacao = ValidarCabecalhoOperacao(
            input.InsumoId,
            input.ResponsavelId,
            chave,
            input.Motivo,
            input.Observacao);
        if (validacao is not null)
        {
            return OperacaoEstoqueInsumoResult.Failure(
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                validacao);
        }

        if (input.Tipo is not (TipoMovimentoInsumo.SaidaManual
            or TipoMovimentoInsumo.Perda
            or TipoMovimentoInsumo.Descarte)
            || input.QuantidadeOriginal <= 0m
            || !Enum.IsDefined(input.UnidadeOriginal))
        {
            return OperacaoEstoqueInsumoResult.Failure(
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                "Tipo, unidade e quantidade de saída são inválidos.");
        }

        var fingerprint = CalcularFingerprint(input);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"stock-entry:{input.ResponsavelId:D}:{chave}",
                cancellationToken).ConfigureAwait(false);
            var replay = await BuscarResultadoRepetidoAsync(
                input.ResponsavelId,
                chave!,
                fingerprint,
                cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return replay;
            }

            await AdquirirLockAsync($"stock-supply:{input.InsumoId:D}", cancellationToken)
                .ConfigureAwait(false);
            var insumo = await _dbContext.InsumosEstoque
                .SingleOrDefaultAsync(item => item.Id == input.InsumoId, cancellationToken)
                .ConfigureAwait(false);
            if (insumo is null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.InsumoNaoEncontrado,
                    "O insumo informado não existe.").ConfigureAwait(false);
            }

            var (lote, erroLote) = await ResolverLoteAsync(
                insumo,
                input.LoteId,
                cancellationToken).ConfigureAwait(false);
            if (erroLote is not null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    erroLote.Value,
                    "Informe um lote válido que pertença ao insumo.").ConfigureAwait(false);
            }

            QuantidadeEstoque quantidade;
            try
            {
                quantidade = QuantidadeEstoque.Criar(input.QuantidadeOriginal, input.UnidadeOriginal)
                    .ExigirPositiva(nameof(input.QuantidadeOriginal));
            }
            catch (ArgumentException exception)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                    exception.Message).ConfigureAwait(false);
            }

            if (quantidade.UnidadeBase != insumo.UnidadeBase)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                    "A quantidade informada não é compatível com a unidade-base do insumo.")
                    .ConfigureAwait(false);
            }

            if (input.Tipo == TipoMovimentoInsumo.SaidaManual
                && lote is not null
                && !lote.EstaElegivelEm(DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime)))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.LoteInelegivel,
                    "Uma saída operacional exige lote elegível; perda ou descarte registra lote inelegível.")
                    .ConfigureAwait(false);
            }

            var saldo = await ObterSaldoAsync(insumo.Id, lote?.Id, cancellationToken)
                .ConfigureAwait(false);
            var anterior = saldo?.QuantidadeFisica ?? QuantidadeEstoque.Criar(0m, insumo.UnidadeBase);
            if (quantidade.ValorBase > anterior.ValorBase)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.SaldoInsuficiente,
                    "A saída excede o saldo físico da posição informada.").ConfigureAwait(false);
            }

            var movimento = MovimentoInsumo.CriarSaida(
                insumo,
                lote,
                input.ResponsavelId,
                input.Tipo,
                input.QuantidadeOriginal,
                input.UnidadeOriginal,
                quantidade,
                anterior,
                input.Motivo,
                input.Observacao,
                _timeProvider.GetUtcNow(),
                chave!,
                fingerprint);
            AplicarMovimento(insumo, lote, saldo, movimento);
            _dbContext.MovimentosInsumo.Add(movimento);

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return OperacaoEstoqueInsumoResult.Success(movimento);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "O estoque foi alterado concorrentemente; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000 or 2601 or 2627)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException
            { Number: 2601 or 2627 })
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<OperacaoEstoqueInsumoResult> AjustarContagemAsync(
        AjustarContagemInsumoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chave = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        var validacao = ValidarCabecalhoOperacao(
            input.InsumoId,
            input.ResponsavelId,
            chave,
            input.Motivo,
            input.Observacao);
        if (validacao is not null)
        {
            return OperacaoEstoqueInsumoResult.Failure(
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                validacao);
        }

        if (input.QuantidadeEncontrada < 0m
            || !Enum.IsDefined(input.UnidadeOriginal)
            || input.VersaoSaldoEsperada is { Length: not 8 })
        {
            return OperacaoEstoqueInsumoResult.Failure(
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                "A contagem física deve ser não negativa, usar unidade válida e informar uma versão SQL válida.");
        }

        var fingerprint = CalcularFingerprint(input);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"stock-entry:{input.ResponsavelId:D}:{chave}",
                cancellationToken).ConfigureAwait(false);
            var replay = await BuscarResultadoRepetidoAsync(
                input.ResponsavelId,
                chave!,
                fingerprint,
                cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return replay;
            }

            await AdquirirLockAsync($"stock-supply:{input.InsumoId:D}", cancellationToken)
                .ConfigureAwait(false);
            var insumo = await _dbContext.InsumosEstoque
                .SingleOrDefaultAsync(item => item.Id == input.InsumoId, cancellationToken)
                .ConfigureAwait(false);
            if (insumo is null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.InsumoNaoEncontrado,
                    "O insumo informado não existe.").ConfigureAwait(false);
            }

            var (lote, erroLote) = await ResolverLoteAsync(
                insumo,
                input.LoteId,
                cancellationToken).ConfigureAwait(false);
            if (erroLote is not null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    erroLote.Value,
                    "Informe um lote válido que pertença ao insumo.").ConfigureAwait(false);
            }

            QuantidadeEstoque contada;
            try
            {
                contada = QuantidadeEstoque.Criar(input.QuantidadeEncontrada, input.UnidadeOriginal);
            }
            catch (ArgumentException exception)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                    exception.Message).ConfigureAwait(false);
            }

            if (contada.UnidadeBase != insumo.UnidadeBase)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                    "A contagem não é compatível com a unidade-base do insumo.")
                    .ConfigureAwait(false);
            }

            var saldo = await ObterSaldoAsync(insumo.Id, lote?.Id, cancellationToken)
                .ConfigureAwait(false);
            if (!VersaoSaldoCorresponde(saldo, input.VersaoSaldoEsperada))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                    "A posição mudou desde a leitura da contagem; atualize o saldo e refaça a contagem.")
                    .ConfigureAwait(false);
            }

            var anterior = saldo?.QuantidadeFisica ?? QuantidadeEstoque.Criar(0m, insumo.UnidadeBase);
            if (contada.ValorBase == anterior.ValorBase)
            {
                _dbContext.AjustesSemDiferencaInsumo.Add(AjusteSemDiferencaInsumo.Criar(
                    input.ResponsavelId,
                    chave!,
                    fingerprint,
                    _timeProvider.GetUtcNow()));
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return OperacaoEstoqueInsumoResult.NoDifference();
            }

            var diferenca = Math.Abs(contada.ValorBase - anterior.ValorBase);
            var movimento = MovimentoInsumo.CriarAjuste(
                insumo,
                lote,
                input.ResponsavelId,
                input.QuantidadeEncontrada,
                input.UnidadeOriginal,
                contada,
                QuantidadeEstoque.Criar(diferenca, insumo.UnidadeBase),
                anterior,
                input.Motivo,
                input.Observacao,
                _timeProvider.GetUtcNow(),
                chave!,
                fingerprint);
            AplicarMovimento(insumo, lote, saldo, movimento);
            _dbContext.MovimentosInsumo.Add(movimento);

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return OperacaoEstoqueInsumoResult.Success(movimento);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "A posição foi alterada concorrentemente; refaça a contagem com nova chave.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000 or 2601 or 2627)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException
            { Number: 2601 or 2627 })
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<OperacaoEstoqueInsumoResult> EstornarMovimentoAsync(
        EstornarMovimentoInsumoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chave = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        var validacao = ValidarCabecalhoOperacao(
            null,
            input.ResponsavelId,
            chave,
            input.Motivo,
            input.Observacao);
        if (input.MovimentoOriginalId == Guid.Empty || validacao is not null)
        {
            return OperacaoEstoqueInsumoResult.Failure(
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                validacao ?? "O movimento original é obrigatório.");
        }

        var fingerprint = CalcularFingerprint(input);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"stock-entry:{input.ResponsavelId:D}:{chave}",
                cancellationToken).ConfigureAwait(false);
            var replay = await BuscarResultadoRepetidoAsync(
                input.ResponsavelId,
                chave!,
                fingerprint,
                cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return replay;
            }

            var original = await _dbContext.MovimentosInsumo
                .SingleOrDefaultAsync(
                    movimento => movimento.Id == input.MovimentoOriginalId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (original is null || original.Tipo == TipoMovimentoInsumo.Estorno)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.MovimentoNaoEncontrado,
                    "O movimento não existe ou não é elegível para estorno.")
                    .ConfigureAwait(false);
            }

            await AdquirirLockAsync($"stock-supply:{original.InsumoId:D}", cancellationToken)
                .ConfigureAwait(false);
            var insumo = await _dbContext.InsumosEstoque
                .SingleOrDefaultAsync(item => item.Id == original.InsumoId, cancellationToken)
                .ConfigureAwait(false);
            if (insumo is null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.InsumoNaoEncontrado,
                    "O insumo proprietário não existe.").ConfigureAwait(false);
            }

            var (lote, erroLote) = await ResolverLoteAsync(
                insumo,
                original.LoteId,
                cancellationToken).ConfigureAwait(false);
            if (erroLote is not null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    erroLote.Value,
                    "O lote original não está disponível para estorno.").ConfigureAwait(false);
            }

            if (await _dbContext.MovimentosInsumo.AnyAsync(
                    movimento => movimento.MovimentoOriginalId == original.Id,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.MovimentoJaEstornado,
                    "O movimento original já possui estorno.")
                    .ConfigureAwait(false);
            }

            var saldo = await ObterSaldoAsync(insumo.Id, lote?.Id, cancellationToken)
                .ConfigureAwait(false);
            var anterior = saldo?.QuantidadeFisica ?? QuantidadeEstoque.Criar(0m, insumo.UnidadeBase);
            var direcaoEstorno = original.Direcao == DirecaoMovimentoInsumo.Entrada
                ? DirecaoMovimentoInsumo.Saida
                : DirecaoMovimentoInsumo.Entrada;
            if (direcaoEstorno == DirecaoMovimentoInsumo.Saida
                && original.QuantidadeBase.ValorBase > anterior.ValorBase)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    OperacaoEstoqueInsumoStatus.SaldoInsuficiente,
                    "O saldo atual não permite estornar esta movimentação.")
                    .ConfigureAwait(false);
            }

            var movimento = MovimentoInsumo.CriarEstorno(
                insumo,
                lote,
                input.ResponsavelId,
                original,
                anterior,
                input.Motivo,
                input.Observacao,
                _timeProvider.GetUtcNow(),
                chave!,
                fingerprint);
            AplicarMovimento(insumo, lote, saldo, movimento);
            _dbContext.MovimentosInsumo.Add(movimento);

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return OperacaoEstoqueInsumoResult.Success(movimento);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "O estoque foi alterado concorrentemente; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000 or 2601 or 2627)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException
            { Number: 2601 or 2627 })
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                OperacaoEstoqueInsumoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<AtualizarPoliticaLoteInsumoStatus> AtualizarPoliticaLoteAsync(
        Guid insumoId,
        bool controlaLote,
        bool validadeObrigatoria,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync($"stock-supply:{insumoId:D}", cancellationToken)
                .ConfigureAwait(false);
            var insumo = await _dbContext.InsumosEstoque
                .SingleOrDefaultAsync(item => item.Id == insumoId, cancellationToken)
                .ConfigureAwait(false);
            if (insumo is null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return AtualizarPoliticaLoteInsumoStatus.InsumoNaoEncontrado;
            }

            if (validadeObrigatoria && !controlaLote)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return AtualizarPoliticaLoteInsumoStatus.ConfiguracaoInvalida;
            }

            if (insumo.ControlaLote != controlaLote
                || insumo.ValidadeObrigatoria != validadeObrigatoria)
            {
                var possuiHistorico = await _dbContext.LotesInsumo.AnyAsync(
                        lote => lote.InsumoId == insumoId,
                        cancellationToken)
                    .ConfigureAwait(false)
                    || await _dbContext.SaldosInsumo.AnyAsync(
                        saldo => saldo.InsumoId == insumoId,
                        cancellationToken)
                    .ConfigureAwait(false)
                    || await _dbContext.MovimentosInsumo.AnyAsync(
                        movimento => movimento.InsumoId == insumoId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (possuiHistorico)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return AtualizarPoliticaLoteInsumoStatus.HistoricoExistente;
                }

                insumo.AtualizarPoliticaLoteProtegida(controlaLote, validadeObrigatoria);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return AtualizarPoliticaLoteInsumoStatus.Atualizado;
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task AdquirirLockAsync(string resource, CancellationToken cancellationToken)
    {
        var result = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @lockResult < 0
                THROW 51000, 'Could not acquire the stock operation lock.', 1;
            """, cancellationToken).ConfigureAwait(false);
        _ = result;
    }

    private async Task<RegistrarEntradaInsumoResult> ReverterComFalhaAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        RegistrarEntradaInsumoStatus status,
        string erro)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        _dbContext.ChangeTracker.Clear();
        return RegistrarEntradaInsumoResult.Failure(status, erro);
    }

    private async Task<OperacaoEstoqueInsumoResult> ReverterComFalhaAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        OperacaoEstoqueInsumoStatus status,
        string erro)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        _dbContext.ChangeTracker.Clear();
        return OperacaoEstoqueInsumoResult.Failure(status, erro);
    }

    private static async Task ReverterSePossivelAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<OperacaoEstoqueInsumoResult?> BuscarResultadoRepetidoAsync(
        Guid responsavelId,
        string chave,
        byte[] fingerprint,
        CancellationToken cancellationToken)
    {
        var movement = await _dbContext.MovimentosInsumo.SingleOrDefaultAsync(
                item => item.ResponsavelId == responsavelId && item.ChaveIdempotencia == chave,
                cancellationToken)
            .ConfigureAwait(false);
        if (movement is not null)
        {
            return CryptographicOperations.FixedTimeEquals(movement.FingerprintRequisicao, fingerprint)
                ? OperacaoEstoqueInsumoResult.Success(movement)
                : OperacaoEstoqueInsumoResult.Failure(
                    OperacaoEstoqueInsumoStatus.ConflitoIdempotencia,
                    "A chave de idempotência já foi usada com outros dados.");
        }

        var noDifference = await _dbContext.AjustesSemDiferencaInsumo.SingleOrDefaultAsync(
                item => item.ResponsavelId == responsavelId && item.ChaveIdempotencia == chave,
                cancellationToken)
            .ConfigureAwait(false);
        if (noDifference is null)
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(noDifference.FingerprintRequisicao, fingerprint)
            ? OperacaoEstoqueInsumoResult.NoDifference()
            : OperacaoEstoqueInsumoResult.Failure(
                OperacaoEstoqueInsumoStatus.ConflitoIdempotencia,
                "A chave de idempotência já foi usada com outros dados.");
    }

    private async Task<(LoteInsumo? Lote, OperacaoEstoqueInsumoStatus? Erro)> ResolverLoteAsync(
        InsumoEstoque insumo,
        Guid? loteId,
        CancellationToken cancellationToken)
    {
        if (insumo.ControlaLote != loteId.HasValue)
        {
            return (null, OperacaoEstoqueInsumoStatus.OperacaoInvalida);
        }

        if (loteId is null)
        {
            return (null, null);
        }

        var lote = await _dbContext.LotesInsumo.SingleOrDefaultAsync(
                item => item.Id == loteId.Value && item.InsumoId == insumo.Id,
                cancellationToken)
            .ConfigureAwait(false);
        return lote is null
            ? (null, OperacaoEstoqueInsumoStatus.LoteNaoEncontrado)
            : (lote, null);
    }

    private Task<SaldoInsumo?> ObterSaldoAsync(
        Guid insumoId,
        Guid? loteId,
        CancellationToken cancellationToken) =>
        _dbContext.SaldosInsumo.SingleOrDefaultAsync(
            item => item.InsumoId == insumoId && item.LoteId == loteId,
            cancellationToken);

    private bool VersaoSaldoCorresponde(SaldoInsumo? saldo, byte[]? versaoEsperada)
    {
        if (saldo is null)
        {
            return versaoEsperada is null;
        }

        if (versaoEsperada is null)
        {
            return false;
        }

        var versaoAtual = _dbContext.Entry(saldo).Property<byte[]>("Versao").CurrentValue;
        return versaoAtual is not null
            && CryptographicOperations.FixedTimeEquals(versaoAtual, versaoEsperada);
    }

    private SaldoInsumo AplicarMovimento(
        InsumoEstoque insumo,
        LoteInsumo? lote,
        SaldoInsumo? saldo,
        MovimentoInsumo movimento)
    {
        if (saldo is null)
        {
            saldo = SaldoInsumo.CriarZero(insumo, lote);
            _dbContext.SaldosInsumo.Add(saldo);
        }

        insumo.AplicarMovimento(saldo, movimento);
        return saldo;
    }

    private static string? ValidarCabecalhoOperacao(
        Guid? insumoId,
        Guid responsavelId,
        string? chave,
        string? motivo,
        string? observacao)
    {
        if (insumoId == Guid.Empty || responsavelId == Guid.Empty)
        {
            return "Insumo e responsável devem ter identificadores válidos.";
        }

        if (chave is null)
        {
            return "A chave de idempotência é obrigatória e deve ter até 128 caracteres.";
        }

        if (string.IsNullOrWhiteSpace(motivo)
            || motivo.Length > MovimentoInsumo.MotivoMaxLength)
        {
            return $"O motivo é obrigatório e deve ter até {MovimentoInsumo.MotivoMaxLength} caracteres.";
        }

        if (observacao?.Length > MovimentoInsumo.ObservacaoMaxLength)
        {
            return $"A observação não pode exceder {MovimentoInsumo.ObservacaoMaxLength} caracteres.";
        }

        return null;
    }

    private static string? NormalizarChaveIdempotencia(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
        {
            return null;
        }

        var normalized = string.Join(' ', chave.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MovimentoInsumo.ChaveIdempotenciaMaxLength
            ? normalized
            : null;
    }

    private static string? ValidarTipoQuantidadeEObservacao(RegistrarEntradaInsumoInput input)
    {
        if (input.Tipo is not (TipoMovimentoInsumo.EstoqueInicial or TipoMovimentoInsumo.Aquisicao))
        {
            return "Tipo de movimento não permitido no registro de entrada.";
        }

        if (input.ResponsavelId == Guid.Empty)
        {
            return "O responsável é obrigatório.";
        }

        if ((input.FormaCompraId is null) == (input.UnidadeOriginal is null))
        {
            return "Informe exatamente uma forma de compra ou uma unidade física.";
        }

        if (input.FormaCompraId == Guid.Empty)
        {
            return "A forma de compra é inválida.";
        }

        if (input.UnidadeOriginal is not null
            && !Enum.IsDefined(input.UnidadeOriginal.Value))
        {
            return "A unidade física é inválida.";
        }

        if (input.QuantidadeOriginal <= 0m)
        {
            return "A quantidade informada deve ser positiva.";
        }

        if (input.Observacao?.Length > MovimentoInsumo.ObservacaoMaxLength)
        {
            return $"A observação não pode exceder {MovimentoInsumo.ObservacaoMaxLength} caracteres.";
        }

        return null;
    }

    private static ResultadoConversaoCompraInsumo? ResolverQuantidade(
        InsumoEstoque insumo,
        RegistrarEntradaInsumoInput input,
        out QuantidadeEstoque? quantidadeBase,
        out string? erro)
    {
        erro = null;
        try
        {
            if (input.FormaCompraId is Guid formaId)
            {
                var resultado = insumo.ConverterCompra(formaId, input.QuantidadeOriginal);
                quantidadeBase = resultado.QuantidadeBase;
                return resultado;
            }

            var quantidade = QuantidadeEstoque.Criar(input.QuantidadeOriginal, input.UnidadeOriginal!.Value)
                .ExigirPositiva(nameof(input.QuantidadeOriginal));
            if (quantidade.UnidadeBase != insumo.UnidadeBase)
            {
                erro = "A quantidade informada não é compatível com a unidade-base do insumo.";
                quantidadeBase = null;
                return null;
            }

            quantidadeBase = quantidade;
            return null;
        }
        catch (ArgumentException exception)
        {
            erro = exception.Message;
            quantidadeBase = null;
            return null;
        }
    }

    private static byte[] CalcularFingerprint(RegistrarEntradaInsumoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            Escrever(writer, input.InsumoId);
            Escrever(writer, input.ResponsavelId);
            Escrever(writer, (short)input.Tipo);
            Escrever(writer, input.QuantidadeOriginal);
            Escrever(writer, input.UnidadeOriginal is null ? null : (short)input.UnidadeOriginal.Value);
            Escrever(writer, input.FormaCompraId);
            Escrever(writer, input.LoteId);
            Escrever(writer, input.IdentificacaoNovoLote);
            Escrever(writer, input.DataEntradaNovoLote);
            Escrever(writer, input.DataValidadeNovoLote);
            Escrever(writer, input.FornecedorNovoLote);
            Escrever(writer, input.NovoLoteBloqueado);
            Escrever(writer, input.CustoTotal);
            Escrever(writer, input.Observacao);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static byte[] CalcularFingerprint(RegistrarSaidaInsumoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            Escrever(writer, input.InsumoId);
            Escrever(writer, input.ResponsavelId);
            Escrever(writer, (short)input.Tipo);
            Escrever(writer, input.QuantidadeOriginal);
            Escrever(writer, (short)input.UnidadeOriginal);
            Escrever(writer, input.LoteId);
            Escrever(writer, input.Motivo);
            Escrever(writer, input.Observacao);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static byte[] CalcularFingerprint(AjustarContagemInsumoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            Escrever(writer, input.InsumoId);
            Escrever(writer, input.ResponsavelId);
            Escrever(writer, (short)TipoMovimentoInsumo.AjusteInventario);
            Escrever(writer, input.QuantidadeEncontrada);
            Escrever(writer, (short)input.UnidadeOriginal);
            Escrever(writer, input.LoteId);
            Escrever(writer, input.VersaoSaldoEsperada);
            Escrever(writer, input.Motivo);
            Escrever(writer, input.Observacao);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static byte[] CalcularFingerprint(EstornarMovimentoInsumoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            Escrever(writer, (short)TipoMovimentoInsumo.Estorno);
            Escrever(writer, input.MovimentoOriginalId);
            Escrever(writer, input.ResponsavelId);
            Escrever(writer, input.Motivo);
            Escrever(writer, input.Observacao);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static void Escrever(BinaryWriter writer, Guid value) => writer.Write(value.ToByteArray());
    private static void Escrever(BinaryWriter writer, Guid? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            Escrever(writer, value.Value);
        }
    }

    private static void Escrever(BinaryWriter writer, short value) => writer.Write(value);
    private static void Escrever(BinaryWriter writer, short? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value);
        }
    }

    private static void Escrever(BinaryWriter writer, decimal value) =>
        Escrever(writer, value.ToString("G29", CultureInfo.InvariantCulture));
    private static void Escrever(BinaryWriter writer, decimal? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            Escrever(writer, value.Value);
        }
    }

    private static void Escrever(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static void Escrever(BinaryWriter writer, byte[]? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value.Length);
            writer.Write(value);
        }
    }

    private static void Escrever(BinaryWriter writer, DateOnly? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value.DayNumber);
        }
    }

    private static void Escrever(BinaryWriter writer, bool value) => writer.Write(value);
}
