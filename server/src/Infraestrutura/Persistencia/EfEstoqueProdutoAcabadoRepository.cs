using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IEstoqueProdutoAcabadoRepository
{
    private readonly XaquiamuniDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICalendarioOperacionalEstoque _calendario;
    private readonly bool _promocoesHabilitadas;
    private readonly IDataProtector? _ofertasEntrega;
    private readonly IReadOnlyDictionary<string, string> _municipiosPorCep;

    public EfEstoqueProdutoAcabadoRepository(
        XaquiamuniDbContext dbContext,
        TimeProvider timeProvider,
        ICalendarioOperacionalEstoque calendario,
        IOptions<PromocoesComerciaisOptions>? promocoes = null,
        IDataProtectionProvider? protecao = null,
        IOptions<EntregaCheckoutOptions>? entrega = null,
        IOptions<CorreiosManualOptions>? correiosManual = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _municipiosPostais = correiosManual?.Value.MunicipiosPorCep ?? [];
        _calendario = calendario;
        _promocoesHabilitadas = promocoes?.Value.Habilitadas == true;
        _ofertasEntrega = protecao?.CreateProtector("Xaquiamuni.Checkout.OfertaEntrega.v1");
        _municipiosPorCep = entrega?.Value.MunicipiosPorCep ?? new Dictionary<string, string>();
    }

    public async Task<ConfigurarPoliticaProdutoAcabadoResult> ConfigurarPoliticaAsync(
        ConfigurarPoliticaProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ProdutoVarianteId == Guid.Empty)
        {
            return new ConfigurarPoliticaProdutoAcabadoResult(
                ConfigurarPoliticaProdutoAcabadoStatus.DadosInvalidos,
                null,
                "O identificador da variante é obrigatório.");
        }

        if (input.EstoqueMinimo < 0 || input.PrazoValidadePadraoDias < 0)
        {
            return new ConfigurarPoliticaProdutoAcabadoResult(
                ConfigurarPoliticaProdutoAcabadoStatus.DadosInvalidos,
                null,
                "Estoque mínimo e prazo de validade não podem ser negativos.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockVarianteAsync(
                input.ProdutoVarianteId,
                cancellationToken).ConfigureAwait(false);
            var variante = await _dbContext.ProdutoVariantes
                .SingleOrDefaultAsync(
                    item => item.Id == input.ProdutoVarianteId,
                    cancellationToken).ConfigureAwait(false);
            if (variante is null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return new ConfigurarPoliticaProdutoAcabadoResult(
                    ConfigurarPoliticaProdutoAcabadoStatus.VarianteNaoEncontrada,
                    null,
                    "A variante informada não existe.");
            }

            var politica = await _dbContext.PoliticasEstoqueProdutoVariante
                .SingleOrDefaultAsync(
                    item => item.ProdutoVarianteId == variante.Id,
                    cancellationToken).ConfigureAwait(false);
            if (politica is null)
            {
                politica = PoliticaEstoqueProdutoVariante.Criar(
                    variante,
                    input.EstoqueMinimo,
                    input.PrazoValidadePadraoDias);
                _dbContext.PoliticasEstoqueProdutoVariante.Add(politica);
            }
            else
            {
                politica.Atualizar(
                    variante,
                    input.EstoqueMinimo,
                    input.PrazoValidadePadraoDias);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return new ConfigurarPoliticaProdutoAcabadoResult(
                ConfigurarPoliticaProdutoAcabadoStatus.Sucesso,
                politica,
                null);
        }
        catch (ArgumentException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return new ConfigurarPoliticaProdutoAcabadoResult(
                ConfigurarPoliticaProdutoAcabadoStatus.DadosInvalidos,
                null,
                exception.Message);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<RegistrarEntradaProdutoAcabadoResult> RegistrarEntradaAsync(
        RegistrarEntradaProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chaveIdempotencia = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        if (input.ProdutoVarianteId == Guid.Empty || input.ResponsavelId == Guid.Empty)
        {
            return Falha(
                RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                "Variante e responsável são obrigatórios.");
        }

        if (chaveIdempotencia is null)
        {
            return Falha(
                RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                "A chave de idempotência é obrigatória e deve ter até 128 caracteres.");
        }

        var fingerprint = CalcularFingerprint(input);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"finished-entry:{input.ResponsavelId:D}:{chaveIdempotencia}",
                cancellationToken).ConfigureAwait(false);

            var existente = await _dbContext.MovimentosProdutoAcabado
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
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaProdutoAcabadoStatus.ConflitoIdempotencia,
                        "A chave de idempotência já foi usada com outros dados.")
                        .ConfigureAwait(false);
                }

                var loteExistente = await _dbContext.LotesProdutoAcabado
                    .SingleAsync(lote => lote.Id == existente.LoteId, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return RegistrarEntradaProdutoAcabadoResult.Success(
                    existente,
                    loteExistente,
                    repetida: true);
            }

            if (await _dbContext.ContagensSemDiferencaProdutoAcabado.AnyAsync(
                    item => item.ResponsavelId == input.ResponsavelId
                        && item.ChaveIdempotencia == chaveIdempotencia,
                    cancellationToken).ConfigureAwait(false))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaProdutoAcabadoStatus.ConflitoIdempotencia,
                    "A chave de idempotência já foi usada por outra operação.")
                    .ConfigureAwait(false);
            }

            await AdquirirLockVarianteAsync(
                input.ProdutoVarianteId,
                cancellationToken).ConfigureAwait(false);

            if (input.Tipo is not (TipoMovimentoProdutoAcabado.EstoqueInicial
                or TipoMovimentoProdutoAcabado.EntradaManual))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                    "Tipo de movimento não permitido no registro de entrada.")
                    .ConfigureAwait(false);
            }

            if (input.Quantidade <= 0
                || !Enum.IsDefined(input.CondicaoDestino)
                || input.Observacao?.Length > MovimentoProdutoAcabado.ObservacaoMaxLength)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                    "Quantidade, condição ou observação da entrada é inválida.")
                    .ConfigureAwait(false);
            }

            var variante = await _dbContext.ProdutoVariantes
                .SingleOrDefaultAsync(
                    item => item.Id == input.ProdutoVarianteId,
                    cancellationToken).ConfigureAwait(false);
            if (variante is null)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaProdutoAcabadoStatus.VarianteNaoEncontrada,
                    "A variante informada não existe.").ConfigureAwait(false);
            }

            LoteProdutoAcabado lote;
            var loteCriado = input.LoteId is null;
            if (input.LoteId is Guid loteId)
            {
                if (input.DataFabricacaoNovoLote is not null || input.DataValidadeNovoLote is not null || input.SemControleValidade)
                {
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                        "Informe um lote existente ou os dados de um novo lote, não ambos.")
                        .ConfigureAwait(false);
                }

                var loteExistente = await _dbContext.LotesProdutoAcabado
                    .SingleOrDefaultAsync(item => item.Id == loteId, cancellationToken)
                    .ConfigureAwait(false);
                if (loteExistente is null)
                {
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaProdutoAcabadoStatus.LoteNaoEncontrado,
                        "O lote informado não existe.").ConfigureAwait(false);
                }

                if (loteExistente.ProdutoVarianteId != variante.Id)
                {
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaProdutoAcabadoStatus.LoteDeOutraVariante,
                        "O lote informado pertence a outra variante.").ConfigureAwait(false);
                }

                lote = loteExistente;
            }
            else
            {
                var fabricacao = input.DataFabricacaoNovoLote ?? (input.SemControleValidade ? _calendario.ObterDataAtual() : (DateOnly?)null);
                if (fabricacao is not DateOnly dataFabricacao)
                {
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                        "A fabricação é obrigatória para criar um lote.")
                        .ConfigureAwait(false);
                }

                try
                {
                    lote = await CriarLoteEntradaAsync(variante, dataFabricacao, input.DataValidadeNovoLote,
                        input.SemControleValidade, cancellationToken).ConfigureAwait(false);
                }
                catch (ArgumentException exception)
                {
                    return await ReverterComFalhaAsync(
                        transaction,
                        RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                        exception.Message).ConfigureAwait(false);
                }
            }

            var saldo = await _dbContext.SaldosProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.ProdutoVarianteId == variante.Id && item.LoteId == lote.Id,
                    cancellationToken).ConfigureAwait(false);
            var saldoCriado = saldo is null;
            saldo ??= SaldoLoteProdutoAcabado.CriarZero(lote);

            if (input.Tipo == TipoMovimentoProdutoAcabado.EstoqueInicial
                && (await _dbContext.MovimentosProdutoAcabado.AnyAsync(
                        movimento => movimento.ProdutoVarianteId == variante.Id
                            && movimento.LoteId == lote.Id,
                        cancellationToken).ConfigureAwait(false)
                    || saldo.QuantidadeFisica != 0))
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaProdutoAcabadoStatus.EstoqueInicialJaRegistrado,
                    "O estoque inicial somente pode abrir um lote sem saldo ou movimentos anteriores.")
                    .ConfigureAwait(false);
            }

            if (loteCriado)
            {
                _dbContext.LotesProdutoAcabado.Add(lote);
            }

            if (saldoCriado)
            {
                _dbContext.SaldosProdutoAcabado.Add(saldo);
            }

            MovimentoProdutoAcabado movimento;
            try
            {
                movimento = AplicarEntradaAcabado(
                    saldo,
                    lote,
                    input.Quantidade,
                    input.CondicaoDestino,
                    input.Tipo,
                    input.ResponsavelId,
                    input.Motivo,
                    _timeProvider.GetUtcNow(),
                    chaveIdempotencia,
                    fingerprint,
                    input.Observacao);
            }
            catch (ArgumentException exception)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                    exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                return await ReverterComFalhaAsync(
                    transaction,
                    RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                    exception.Message).ConfigureAwait(false);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return RegistrarEntradaProdutoAcabadoResult.Success(movimento, lote);
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
                RegistrarEntradaProdutoAcabadoStatus.ConflitoConcorrencia,
                "O estoque foi alterado concorrentemente; repita a operação com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaProdutoAcabadoStatus.ConflitoConcorrencia,
                "A entrada conflitou com uma chave única do estoque; repita a operação com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaProdutoAcabadoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await ReverterComFalhaAsync(
                transaction,
                RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<OperacaoProdutoAcabadoResult> RegistrarOperacaoAsync(
        RegistrarOperacaoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chave = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        if (input.ProdutoVarianteId == Guid.Empty
            || input.LoteId == Guid.Empty
            || input.ResponsavelId == Guid.Empty)
        {
            return OperacaoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "Variante, lote e responsável são obrigatórios.");
        }

        if (chave is null)
        {
            return OperacaoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "A chave de idempotência é obrigatória e deve ter até 128 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(input.Motivo)
            || input.Motivo.Length > MovimentoProdutoAcabado.MotivoMaxLength)
        {
            return OperacaoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                $"O motivo é obrigatório e deve ter até {MovimentoProdutoAcabado.MotivoMaxLength} caracteres.");
        }

        if (input.Observacao?.Length > MovimentoProdutoAcabado.ObservacaoMaxLength)
        {
            return OperacaoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                $"A observação não pode exceder {MovimentoProdutoAcabado.ObservacaoMaxLength} caracteres.");
        }

        if (input.Quantidade <= 0)
        {
            return OperacaoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "A quantidade deve ser inteira e positiva.");
        }

        if (input.Tipo is not (TipoMovimentoProdutoAcabado.SaidaManual
            or TipoMovimentoProdutoAcabado.Perda
            or TipoMovimentoProdutoAcabado.Descarte
            or TipoMovimentoProdutoAcabado.TransferenciaCondicao))
        {
            return OperacaoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "Tipo de movimento não permitido nesta operação.");
        }

        var fingerprint = CalcularFingerprint(input);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"finished-entry:{input.ResponsavelId:D}:{chave}",
                cancellationToken).ConfigureAwait(false);

            var existente = await _dbContext.MovimentosProdutoAcabado
                .SingleOrDefaultAsync(
                    movimento => movimento.ResponsavelId == input.ResponsavelId
                        && movimento.ChaveIdempotencia == chave,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existente is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        existente.FingerprintRequisicao,
                        fingerprint))
                {
                    return await ReverterOperacaoComFalhaAsync(
                        transaction,
                        OperacaoProdutoAcabadoStatus.ConflitoIdempotencia,
                        "A chave de idempotência já foi usada com outros dados.")
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return OperacaoProdutoAcabadoResult.Success(existente, repetida: true);
            }

            await AdquirirLockVarianteAsync(
                input.ProdutoVarianteId,
                cancellationToken).ConfigureAwait(false);

            var variante = await _dbContext.ProdutoVariantes
                .SingleOrDefaultAsync(
                    item => item.Id == input.ProdutoVarianteId,
                    cancellationToken).ConfigureAwait(false);
            if (variante is null)
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.VarianteNaoEncontrada,
                    "A variante informada não existe.").ConfigureAwait(false);
            }

            var lote = await _dbContext.LotesProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.Id == input.LoteId,
                    cancellationToken).ConfigureAwait(false);
            if (lote is null)
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.LoteNaoEncontrado,
                    "O lote informado não existe.").ConfigureAwait(false);
            }

            if (lote.ProdutoVarianteId != variante.Id)
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.LoteDeOutraVariante,
                    "O lote informado pertence a outra variante.").ConfigureAwait(false);
            }

            var saldo = await _dbContext.SaldosProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.ProdutoVarianteId == variante.Id && item.LoteId == lote.Id,
                    cancellationToken).ConfigureAwait(false);
            if (saldo is null)
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.SaldoInsuficiente,
                    "A posição não possui saldo para esta operação.").ConfigureAwait(false);
            }

            if (input.Tipo == TipoMovimentoProdutoAcabado.SaidaManual
                && (input.CondicaoOrigem != CondicaoEstoqueProdutoAcabado.Vendavel
                    || input.CondicaoDestino is not null))
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                    "Saída manual comercial deve partir exclusivamente da condição vendável.")
                    .ConfigureAwait(false);
            }

            if (input.Tipo == TipoMovimentoProdutoAcabado.TransferenciaCondicao)
            {
                var origem = input.CondicaoOrigem;
                var destino = input.CondicaoDestino;
                var transicaoPermitida = origem == CondicaoEstoqueProdutoAcabado.Vendavel
                        && destino == CondicaoEstoqueProdutoAcabado.Quarentena
                    || origem == CondicaoEstoqueProdutoAcabado.Quarentena
                        && destino == CondicaoEstoqueProdutoAcabado.Vendavel
                    || origem == CondicaoEstoqueProdutoAcabado.Vendavel
                        && destino == CondicaoEstoqueProdutoAcabado.Danificada
                    || origem == CondicaoEstoqueProdutoAcabado.Danificada
                        && destino == CondicaoEstoqueProdutoAcabado.Vendavel;
                if (!transicaoPermitida)
                {
                    return await ReverterOperacaoComFalhaAsync(
                        transaction,
                        OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                        "A transferência entre as condições informadas não é permitida.")
                        .ConfigureAwait(false);
                }
            }

            MovimentoProdutoAcabado movimento;
            try
            {
                movimento = input.Tipo switch
                {
                    TipoMovimentoProdutoAcabado.SaidaManual =>
                        saldo.RegistrarSaidaManual(
                            variante,
                            lote,
                            input.Quantidade,
                            _calendario.ObterDataAtual(),
                            input.ResponsavelId,
                            input.Motivo,
                            _timeProvider.GetUtcNow(),
                            chave,
                            fingerprint,
                            input.Observacao),
                    TipoMovimentoProdutoAcabado.Perda =>
                        saldo.RegistrarPerda(
                            lote,
                            input.Quantidade,
                            ValidarCondicaoOrigem(input.CondicaoOrigem),
                            input.ResponsavelId,
                            input.Motivo,
                            _timeProvider.GetUtcNow(),
                            chave,
                            fingerprint,
                            input.Observacao),
                    TipoMovimentoProdutoAcabado.Descarte =>
                        saldo.RegistrarDescarte(
                            lote,
                            input.Quantidade,
                            ValidarCondicaoOrigem(input.CondicaoOrigem),
                            input.ResponsavelId,
                            input.Motivo,
                            _timeProvider.GetUtcNow(),
                            chave,
                            fingerprint,
                            input.Observacao),
                    TipoMovimentoProdutoAcabado.TransferenciaCondicao =>
                        saldo.TransferirCondicao(
                            lote,
                            input.Quantidade,
                            ValidarCondicaoOrigem(input.CondicaoOrigem),
                            ValidarCondicaoDestino(input.CondicaoDestino),
                            input.ResponsavelId,
                            input.Motivo,
                            _timeProvider.GetUtcNow(),
                            chave,
                            fingerprint,
                            input.Observacao),
                    _ => throw new InvalidOperationException("Tipo de operação inválido.")
                };
            }
            catch (ArgumentException exception)
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                    exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("saldo", StringComparison.OrdinalIgnoreCase))
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.SaldoInsuficiente,
                    exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("inativa", StringComparison.OrdinalIgnoreCase))
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.VarianteInativa,
                    exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("vencido", StringComparison.OrdinalIgnoreCase))
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.LoteInelegivel,
                    exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                    exception.Message).ConfigureAwait(false);
            }

            if (await ReduzEstoqueComprometidoAsync(movimento, cancellationToken).ConfigureAwait(false))
            {
                return await ReverterOperacaoComFalhaAsync(
                    transaction, OperacaoProdutoAcabadoStatus.SaldoInsuficiente, MensagemEstoqueComprometido)
                    .ConfigureAwait(false);
            }

            _dbContext.MovimentosProdutoAcabado.Add(movimento);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return OperacaoProdutoAcabadoResult.Success(movimento);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReverterOperacaoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                "O estoque foi alterado concorrentemente; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return await ReverterOperacaoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoIdempotencia,
                "A chave de idempotência já foi usada por outra operação.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return await ReverterOperacaoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                "A operação conflitou com outra alteração de estoque; repita com a mesma chave.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterOperacaoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ContarProdutoAcabadoResult> ContarAsync(
        ContarProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chave = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        if (input.ProdutoVarianteId == Guid.Empty
            || input.LoteId == Guid.Empty
            || input.ResponsavelId == Guid.Empty)
        {
            return ContarProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "Variante, lote e responsável são obrigatórios.");
        }

        if (chave is null)
        {
            return ContarProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "A chave de idempotência é obrigatória e deve ter até 128 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(input.Motivo)
            || input.Motivo.Length > MovimentoProdutoAcabado.MotivoMaxLength)
        {
            return ContarProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                $"O motivo é obrigatório e deve ter até {MovimentoProdutoAcabado.MotivoMaxLength} caracteres.");
        }

        if (input.Observacao?.Length > MovimentoProdutoAcabado.ObservacaoMaxLength)
        {
            return ContarProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                $"A observação não pode exceder {MovimentoProdutoAcabado.ObservacaoMaxLength} caracteres.");
        }

        if (input.QuantidadeVendavel < 0
            || input.QuantidadeDanificada < 0
            || input.QuantidadeQuarentena < 0
            || input.VersaoSaldoEsperada is { Length: not 8 })
        {
            return ContarProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "As quantidades devem ser não negativas e a versão do saldo deve ser válida.");
        }

        try
        {
            _ = checked(
                checked(input.QuantidadeVendavel + input.QuantidadeDanificada)
                + input.QuantidadeQuarentena);
        }
        catch (OverflowException)
        {
            return ContarProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "A soma das quantidades excede o limite inteiro suportado.");
        }

        var fingerprint = CalcularFingerprint(input);
        var chaveMovimentoBase = CriarChavePrimeira(chave);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"finished-entry:{input.ResponsavelId:D}:{chave}",
                cancellationToken).ConfigureAwait(false);

            var marcadorExistente = await _dbContext.ContagensSemDiferencaProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.ResponsavelId == input.ResponsavelId
                        && item.ChaveIdempotencia == chave,
                    cancellationToken).ConfigureAwait(false);
            if (marcadorExistente is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        marcadorExistente.FingerprintRequisicao,
                        fingerprint))
                {
                    return await ReverterContagemComFalhaAsync(
                        transaction,
                        OperacaoProdutoAcabadoStatus.ConflitoIdempotencia,
                        "A chave de idempotência já foi usada com outros dados.")
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return ContarProdutoAcabadoResult.NoDifference(repetida: true);
            }

            var movimentosExistentes = await _dbContext.MovimentosProdutoAcabado
                .Where(movimento =>
                    movimento.ResponsavelId == input.ResponsavelId
                    && movimento.Tipo == TipoMovimentoProdutoAcabado.AjusteInventario
                    && (movimento.ChaveIdempotencia == chaveMovimentoBase
                        || movimento.ChaveIdempotencia.StartsWith(chaveMovimentoBase + "#")))
                .OrderBy(movimento => movimento.ChaveIdempotencia)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (movimentosExistentes.Count > 0)
            {
                if (movimentosExistentes.Any(movimento =>
                        !CryptographicOperations.FixedTimeEquals(
                            movimento.FingerprintRequisicao,
                            fingerprint)))
                {
                    return await ReverterContagemComFalhaAsync(
                        transaction,
                        OperacaoProdutoAcabadoStatus.ConflitoIdempotencia,
                        "A chave de idempotência já foi usada com outros dados.")
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return ContarProdutoAcabadoResult.Success(movimentosExistentes, repetida: true);
            }

            await AdquirirLockVarianteAsync(
                input.ProdutoVarianteId,
                cancellationToken).ConfigureAwait(false);

            var variante = await _dbContext.ProdutoVariantes
                .SingleOrDefaultAsync(item => item.Id == input.ProdutoVarianteId, cancellationToken)
                .ConfigureAwait(false);
            if (variante is null)
            {
                return await ReverterContagemComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.VarianteNaoEncontrada,
                    "A variante informada não existe.").ConfigureAwait(false);
            }

            var lote = await _dbContext.LotesProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.Id == input.LoteId && item.ProdutoVarianteId == variante.Id,
                    cancellationToken).ConfigureAwait(false);
            if (lote is null)
            {
                return await ReverterContagemComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.LoteNaoEncontrado,
                    "O lote não existe para esta variante.").ConfigureAwait(false);
            }

            var saldo = await _dbContext.SaldosProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.ProdutoVarianteId == variante.Id && item.LoteId == lote.Id,
                    cancellationToken).ConfigureAwait(false);
            if (saldo is null)
            {
                return await ReverterContagemComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                    "Não existe posição de saldo para o lote informado.")
                    .ConfigureAwait(false);
            }

            var versaoAtual = _dbContext.Entry(saldo).Property<byte[]>("Versao").CurrentValue;
            if (versaoAtual is null
                || !CryptographicOperations.FixedTimeEquals(versaoAtual, input.VersaoSaldoEsperada))
            {
                return await ReverterContagemComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                    "A posição mudou desde a leitura da contagem; atualize o saldo e repita.")
                    .ConfigureAwait(false);
            }

            if (await ReduzEstoqueComprometidoAsync(
                    variante.Id, lote.Id, saldo.QuantidadeVendavel, input.QuantidadeVendavel, cancellationToken)
                .ConfigureAwait(false))
            {
                return await ReverterContagemComFalhaAsync(
                    transaction, OperacaoProdutoAcabadoStatus.SaldoInsuficiente, MensagemEstoqueComprometido)
                    .ConfigureAwait(false);
            }

            if (saldo.QuantidadeVendavel == input.QuantidadeVendavel
                && saldo.QuantidadeDanificada == input.QuantidadeDanificada
                && saldo.QuantidadeQuarentena == input.QuantidadeQuarentena)
            {
                var marcadorSemDiferenca = ContagemSemDiferencaProdutoAcabado.Criar(
                    input.ResponsavelId,
                    chave,
                    fingerprint,
                    _timeProvider.GetUtcNow());
                _dbContext.ContagensSemDiferencaProdutoAcabado.Add(marcadorSemDiferenca);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return ContarProdutoAcabadoResult.NoDifference();
            }

            var movimentacoes = new List<MovimentoProdutoAcabado>();
            var contagens = new[]
            {
                (Condicao: CondicaoEstoqueProdutoAcabado.Vendavel, Encontrada: input.QuantidadeVendavel),
                (Condicao: CondicaoEstoqueProdutoAcabado.Danificada, Encontrada: input.QuantidadeDanificada),
                (Condicao: CondicaoEstoqueProdutoAcabado.Quarentena, Encontrada: input.QuantidadeQuarentena)
            };
            var indice = 0;
            foreach (var contagem in contagens)
            {
                var atual = contagem.Condicao switch
                {
                    CondicaoEstoqueProdutoAcabado.Vendavel => saldo.QuantidadeVendavel,
                    CondicaoEstoqueProdutoAcabado.Danificada => saldo.QuantidadeDanificada,
                    _ => saldo.QuantidadeQuarentena
                };
                if (atual == contagem.Encontrada)
                {
                    continue;
                }

                indice++;
                var chaveMovimento = indice == 1
                    ? chaveMovimentoBase
                    : CriarChaveDerivada(chaveMovimentoBase, indice);
                var movimento = saldo.Contar(
                    lote,
                    contagem.Condicao,
                    contagem.Encontrada,
                    input.ResponsavelId,
                    input.Motivo,
                    _timeProvider.GetUtcNow(),
                    chaveMovimento,
                    fingerprint,
                    input.Observacao);
                if (movimento is not null)
                {
                    movimentacoes.Add(movimento);
                    _dbContext.MovimentosProdutoAcabado.Add(movimento);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return ContarProdutoAcabadoResult.Success(movimentacoes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReverterContagemComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                "A posição foi alterada concorrentemente; repita a contagem.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return await ReverterContagemComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoIdempotencia,
                "A chave de idempotência já foi usada por outra operação.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return await ReverterContagemComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                "A contagem conflitou com outra alteração de estoque.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterContagemComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await ReverterContagemComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<EstornarMovimentoProdutoAcabadoResult> EstornarAsync(
        EstornarMovimentoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chave = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        if (input.MovimentoOriginalId == Guid.Empty
            || input.ResponsavelId == Guid.Empty)
        {
            return EstornarMovimentoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "Movimento original e responsável são obrigatórios.");
        }

        if (chave is null)
        {
            return EstornarMovimentoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "A chave de idempotência é obrigatória e deve ter até 128 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(input.Motivo)
            || input.Motivo.Length > MovimentoProdutoAcabado.MotivoMaxLength)
        {
            return EstornarMovimentoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                $"O motivo é obrigatório e deve ter até {MovimentoProdutoAcabado.MotivoMaxLength} caracteres.");
        }

        if (input.Observacao?.Length > MovimentoProdutoAcabado.ObservacaoMaxLength)
        {
            return EstornarMovimentoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                $"A observação não pode exceder {MovimentoProdutoAcabado.ObservacaoMaxLength} caracteres.");
        }

        var fingerprint = CalcularFingerprint(input);
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await AdquirirLockAsync(
                $"finished-entry:{input.ResponsavelId:D}:{chave}",
                cancellationToken).ConfigureAwait(false);

            var existente = await _dbContext.MovimentosProdutoAcabado
                .SingleOrDefaultAsync(
                    movimento => movimento.ResponsavelId == input.ResponsavelId
                        && movimento.ChaveIdempotencia == chave,
                    cancellationToken).ConfigureAwait(false);
            if (existente is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        existente.FingerprintRequisicao,
                        fingerprint))
                {
                    return await ReverterEstornoComFalhaAsync(
                        transaction,
                        OperacaoProdutoAcabadoStatus.ConflitoIdempotencia,
                        "A chave de idempotência já foi usada com outros dados.")
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return EstornarMovimentoProdutoAcabadoResult.Success(existente, repetida: true);
            }

            var original = await _dbContext.MovimentosProdutoAcabado
                .SingleOrDefaultAsync(
                    movimento => movimento.Id == input.MovimentoOriginalId,
                    cancellationToken).ConfigureAwait(false);
            if (original is null
                || original.Tipo == TipoMovimentoProdutoAcabado.Estorno
                || original.ReservaProdutoAcabadoId is not null)
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.MovimentoNaoEncontrado,
                    "O movimento não existe ou não é elegível para estorno.")
                    .ConfigureAwait(false);
            }

            if (await _dbContext.MovimentosProdutoAcabado.AnyAsync(
                    movimento => movimento.MovimentoOriginalId == original.Id,
                    cancellationToken).ConfigureAwait(false))
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.MovimentoJaEstornado,
                    "O movimento original já possui estorno.")
                    .ConfigureAwait(false);
            }

            await AdquirirLockVarianteAsync(
                original.ProdutoVarianteId,
                cancellationToken).ConfigureAwait(false);

            if (await _dbContext.Set<BaixaVendaExterna>().AnyAsync(b => b.MovimentoId == original.Id
                || b.MovimentoCancelamentoId == original.Id, cancellationToken).ConfigureAwait(false))
                return await ReverterEstornoComFalhaAsync(transaction, OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                    "Este movimento pertence a uma venda externa. Use a operação de cancelamento da venda.").ConfigureAwait(false);

            if (await _dbContext.OrdensProducao.AnyAsync(o => o.MovimentoAcabadoId == original.Id, cancellationToken)
                .ConfigureAwait(false))
                return await ReverterEstornoComFalhaAsync(transaction, OperacaoProdutoAcabadoStatus.MovimentoNaoEncontrado,
                    "Entradas de produção exigem uma correção vinculada à ordem.").ConfigureAwait(false);

            var lote = await _dbContext.LotesProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.Id == original.LoteId
                        && item.ProdutoVarianteId == original.ProdutoVarianteId,
                    cancellationToken).ConfigureAwait(false);
            if (lote is null)
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.LoteNaoEncontrado,
                    "O lote original não está disponível.")
                    .ConfigureAwait(false);
            }

            if (await _dbContext.MovimentosProdutoAcabado.AnyAsync(
                    movimento => movimento.LoteId == original.LoteId
                        && movimento.ProdutoVarianteId == original.ProdutoVarianteId
                        && movimento.Id != original.Id
                        && movimento.RegistradoEmUtc >= original.RegistradoEmUtc,
                    cancellationToken).ConfigureAwait(false))
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                    "Existem movimentos posteriores ao original; o estorno foi rejeitado por conflito.")
                    .ConfigureAwait(false);
            }

            var saldo = await _dbContext.SaldosProdutoAcabado
                .SingleOrDefaultAsync(
                    item => item.ProdutoVarianteId == original.ProdutoVarianteId
                        && item.LoteId == original.LoteId,
                    cancellationToken).ConfigureAwait(false);
            if (saldo is null)
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.SaldoInsuficiente,
                    "O lote não possui saldo atual para estorno.")
                    .ConfigureAwait(false);
            }

            MovimentoProdutoAcabado estorno;
            try
            {
                var historico = await _dbContext.MovimentosProdutoAcabado
                    .AsNoTracking()
                    .Where(movimento =>
                        movimento.ProdutoVarianteId == original.ProdutoVarianteId
                        && movimento.LoteId == original.LoteId)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                estorno = saldo.Estornar(
                    lote,
                    original,
                    historico,
                    input.ResponsavelId,
                    input.Motivo,
                    _timeProvider.GetUtcNow(),
                    chave,
                    fingerprint,
                    input.Observacao);
            }
            catch (ArgumentException exception)
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                    exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("saldo", StringComparison.OrdinalIgnoreCase))
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.SaldoInsuficiente,
                    exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction,
                    OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                    exception.Message).ConfigureAwait(false);
            }

            if (await ReduzEstoqueComprometidoAsync(estorno, cancellationToken).ConfigureAwait(false))
            {
                return await ReverterEstornoComFalhaAsync(
                    transaction, OperacaoProdutoAcabadoStatus.SaldoInsuficiente, MensagemEstoqueComprometido)
                    .ConfigureAwait(false);
            }

            _dbContext.MovimentosProdutoAcabado.Add(estorno);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            return EstornarMovimentoProdutoAcabadoResult.Success(estorno);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReverterEstornoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                "O estoque mudou concorrentemente; repita o estorno.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return await ReverterEstornoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.MovimentoJaEstornado,
                "O movimento original já possui estorno.")
                .ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return await ReverterEstornoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.ConflitoConcorrencia,
                "O estorno conflitou com outra alteração de estoque.")
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await ReverterEstornoComFalhaAsync(
                transaction,
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                exception.Message).ConfigureAwait(false);
        }
        catch
        {
            await ReverterSePossivelAsync(transaction).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ContarProdutoAcabadoResult> ReverterContagemComFalhaAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        OperacaoProdutoAcabadoStatus status,
        string erro)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        _dbContext.ChangeTracker.Clear();
        return ContarProdutoAcabadoResult.Failure(status, erro);
    }

    private async Task<EstornarMovimentoProdutoAcabadoResult> ReverterEstornoComFalhaAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        OperacaoProdutoAcabadoStatus status,
        string erro)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        _dbContext.ChangeTracker.Clear();
        return EstornarMovimentoProdutoAcabadoResult.Failure(status, erro);
    }

    private Task AdquirirLockVarianteAsync(Guid varianteId, CancellationToken cancellationToken) =>
        AdquirirLockAsync($"finished-variant:{varianteId:D}", cancellationToken);

    private async Task AdquirirLockAsync(string resource, CancellationToken cancellationToken, bool compartilhado = false)
    {
        var modo = compartilhado ? "Shared" : "Exclusive";
        var result = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = {modo},
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @lockResult < 0
                THROW 51000, 'Could not acquire the finished-goods stock operation lock.', 1;
            """, cancellationToken).ConfigureAwait(false);
        _ = result;
    }

    private async Task<RegistrarEntradaProdutoAcabadoResult> ReverterComFalhaAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        RegistrarEntradaProdutoAcabadoStatus status,
        string erro)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        _dbContext.ChangeTracker.Clear();
        return Falha(status, erro);
    }

    private async Task<OperacaoProdutoAcabadoResult> ReverterOperacaoComFalhaAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        OperacaoProdutoAcabadoStatus status,
        string erro)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        _dbContext.ChangeTracker.Clear();
        return OperacaoProdutoAcabadoResult.Failure(status, erro);
    }

    private static RegistrarEntradaProdutoAcabadoResult Falha(
        RegistrarEntradaProdutoAcabadoStatus status,
        string erro) =>
        RegistrarEntradaProdutoAcabadoResult.Failure(status, erro);

    private static async Task ReverterSePossivelAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private static string? NormalizarChaveIdempotencia(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
        {
            return null;
        }

        var normalizada = NormalizarEspacos(chave);
        return normalizada.Length <= MovimentoProdutoAcabado.ChaveIdempotenciaMaxLength
            ? normalizada
            : null;
    }

    private static byte[] CalcularFingerprint(RegistrarEntradaProdutoAcabadoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(input.ProdutoVarianteId.ToByteArray());
            writer.Write(input.ResponsavelId.ToByteArray());
            writer.Write((short)input.Tipo);
            writer.Write(input.Quantidade);
            writer.Write((short)input.CondicaoDestino);
            EscreverGuidOpcional(writer, input.LoteId);
            EscreverDataOpcional(writer, input.DataFabricacaoNovoLote);
            EscreverDataOpcional(writer, input.DataValidadeNovoLote);
            EscreverTextoOpcional(writer, input.Motivo);
            EscreverTextoOpcional(writer, input.Observacao);
            if (input.SemControleValidade) writer.Write(true);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static byte[] CalcularFingerprint(RegistrarOperacaoProdutoAcabadoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(input.ProdutoVarianteId.ToByteArray());
            writer.Write(input.LoteId.ToByteArray());
            writer.Write(input.ResponsavelId.ToByteArray());
            writer.Write((short)input.Tipo);
            writer.Write(input.Quantidade);
            writer.Write(input.CondicaoOrigem is null ? (short)0 : (short)input.CondicaoOrigem.Value);
            writer.Write(input.CondicaoDestino is null ? (short)0 : (short)input.CondicaoDestino.Value);
            EscreverTextoOpcional(writer, input.Motivo);
            EscreverTextoOpcional(writer, input.Observacao);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static byte[] CalcularFingerprint(ContarProdutoAcabadoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(input.ProdutoVarianteId.ToByteArray());
            writer.Write(input.LoteId.ToByteArray());
            writer.Write(input.ResponsavelId.ToByteArray());
            writer.Write(input.QuantidadeVendavel);
            writer.Write(input.QuantidadeDanificada);
            writer.Write(input.QuantidadeQuarentena);
            EscreverTextoOpcional(writer, input.Motivo);
            EscreverTextoOpcional(writer, input.Observacao);
            writer.Write(input.VersaoSaldoEsperada.Length);
            writer.Write(input.VersaoSaldoEsperada);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static byte[] CalcularFingerprint(EstornarMovimentoProdutoAcabadoInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(input.MovimentoOriginalId.ToByteArray());
            writer.Write(input.ResponsavelId.ToByteArray());
            EscreverTextoOpcional(writer, input.Motivo);
            EscreverTextoOpcional(writer, input.Observacao);
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static string CriarChaveDerivada(string chaveBase, int indice)
    {
        var sufixo = $"#{indice}";
        if (chaveBase.Length + sufixo.Length > MovimentoProdutoAcabado.ChaveIdempotenciaMaxLength)
        {
            var baseHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(chaveBase))).AsSpan(0, 24).ToString();
            return $"cnt-{baseHash}{sufixo}";
        }

        return chaveBase + sufixo;
    }

    private static string CriarChavePrimeira(string chaveBase)
    {
        if (chaveBase.Length + 2 <= MovimentoProdutoAcabado.ChaveIdempotenciaMaxLength)
        {
            return chaveBase;
        }

        var baseHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(chaveBase))).AsSpan(0, 24).ToString();
        return $"cnt-{baseHash}";
    }

    private static CondicaoEstoqueProdutoAcabado ValidarCondicaoOrigem(
        CondicaoEstoqueProdutoAcabado? condicao)
    {
        if (condicao is null || !Enum.IsDefined(condicao.Value))
        {
            throw new ArgumentException("A condição de origem é obrigatória e válida.");
        }

        return condicao.Value;
    }

    private static CondicaoEstoqueProdutoAcabado ValidarCondicaoDestino(
        CondicaoEstoqueProdutoAcabado? condicao)
    {
        if (condicao is null || !Enum.IsDefined(condicao.Value))
        {
            throw new ArgumentException("A condição de destino é obrigatória e válida.");
        }

        return condicao.Value;
    }

    private static void EscreverGuidOpcional(BinaryWriter writer, Guid? valor)
    {
        writer.Write(valor.HasValue);
        if (valor is Guid guid)
        {
            writer.Write(guid.ToByteArray());
        }
    }

    private static void EscreverDataOpcional(BinaryWriter writer, DateOnly? valor)
    {
        writer.Write(valor.HasValue);
        if (valor is DateOnly data)
        {
            writer.Write(data.DayNumber);
        }
    }

    private static void EscreverTextoOpcional(BinaryWriter writer, string? valor)
    {
        var normalizado = string.IsNullOrWhiteSpace(valor) ? null : NormalizarEspacos(valor);
        writer.Write(normalizado is not null);
        if (normalizado is not null)
        {
            writer.Write(normalizado);
        }
    }

    private static string NormalizarEspacos(string valor) =>
        string.Join(' ', valor.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed class EfEstoqueProdutoAcabadoConsultaRepository(
    XaquiamuniDbContext dbContext) : IEstoqueProdutoAcabadoConsultaRepository
{
    public async Task<DetalheEstoqueProdutoAcabadoConsulta?> ConsultarPosicaoVarianteAsync(
        Guid produtoVarianteId,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default)
    {
        var variante = await dbContext.ProdutoVariantes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == produtoVarianteId, cancellationToken)
            .ConfigureAwait(false);
        if (variante is null)
        {
            return null;
        }

        var politica = await dbContext.PoliticasEstoqueProdutoVariante
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ProdutoVarianteId == produtoVarianteId,
                cancellationToken).ConfigureAwait(false);
        var agoraUtc = await dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        var lotes = await ConsultarLotes(produtoVarianteId, agoraUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var posicoes = lotes.Select(lote => MapearPosicao(lote, variante.Ativa, dataReferencia)).ToArray();

        return new DetalheEstoqueProdutoAcabadoConsulta(
            politica is null
                ? null
                : new PoliticaEstoqueProdutoVarianteConsulta(
                    politica.ProdutoVarianteId,
                    politica.EstoqueMinimo,
                    politica.PrazoValidadePadraoDias),
            MapearResumo(produtoVarianteId, variante.Ativa, politica?.EstoqueMinimo, posicoes),
            posicoes);
    }

    public async Task<PosicaoLoteProdutoAcabadoConsulta?> ConsultarPosicaoLoteAsync(
        Guid produtoVarianteId,
        Guid loteId,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default)
    {
        var varianteAtiva = await dbContext.ProdutoVariantes
            .AsNoTracking()
            .Where(item => item.Id == produtoVarianteId)
            .Select(item => (bool?)item.Ativa)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (varianteAtiva is null)
        {
            return null;
        }

        var agoraUtc = await dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        var lote = await ConsultarLotes(produtoVarianteId, agoraUtc, loteId)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return lote is null ? null : MapearPosicao(lote, varianteAtiva.Value, dataReferencia);
    }

    private IQueryable<PosicaoLoteSql> ConsultarLotes(
        Guid produtoVarianteId, DateTimeOffset agoraUtc, Guid? loteId = null)
    {
        var lotes = dbContext.LotesProdutoAcabado.AsNoTracking()
            .Where(lote => lote.ProdutoVarianteId == produtoVarianteId);
        var reservas = dbContext.ReservasProdutoAcabado.ComprometidasEm(agoraUtc)
            .Where(reserva => reserva.ProdutoVarianteId == produtoVarianteId);
        if (loteId is Guid id)
        {
            lotes = lotes.Where(lote => lote.Id == id);
            reservas = reservas.Where(reserva => reserva.LoteId == id);
        }

        var compromissos = reservas.GroupBy(reserva => reserva.LoteId)
            .Select(grupo => new { LoteId = grupo.Key, Quantidade = grupo.Sum(reserva => (long)reserva.Quantidade) });

        return from lote in lotes
               join saldo in dbContext.SaldosProdutoAcabado.AsNoTracking()
                   on new { lote.ProdutoVarianteId, LoteId = lote.Id }
                   equals new { saldo.ProdutoVarianteId, saldo.LoteId } into saldos
               from saldo in saldos.DefaultIfEmpty()
               join compromisso in compromissos on lote.Id equals compromisso.LoteId into reservados
               from compromisso in reservados.DefaultIfEmpty()
               orderby lote.DataValidade, lote.DataFabricacao, lote.Codigo, lote.Id
               select new PosicaoLoteSql(
                   lote,
                   saldo,
                   EF.Property<byte[]>(saldo!, "Versao"),
                   (long?)compromisso.Quantidade ?? 0);
    }

    public async Task<PaginaEstoque<MovimentoProdutoAcabadoConsulta>> ListarMovimentosAsync(
        ListarMovimentosProdutoAcabadoFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var skip = new PaginacaoEstoqueProdutoAcabado(filtro.Pagina, filtro.ItensPorPagina).CalcularSkip();
        var movimentos = dbContext.MovimentosProdutoAcabado
            .AsNoTracking()
            .Where(item => item.ProdutoVarianteId == filtro.ProdutoVarianteId);
        if (filtro.Tipo is not null)
        {
            movimentos = movimentos.Where(item => item.Tipo == filtro.Tipo.Value);
        }

        if (filtro.LoteId is Guid loteId)
        {
            movimentos = movimentos.Where(item => item.LoteId == loteId);
        }

        if (filtro.MovimentoId is Guid movimentoId)
        {
            movimentos = movimentos.Where(item => item.Id == movimentoId);
        }

        var totalItens = await movimentos.CountAsync(cancellationToken).ConfigureAwait(false);
        var itensSql = await (
                from movimento in movimentos
                join lote in dbContext.LotesProdutoAcabado.AsNoTracking()
                    on new { movimento.ProdutoVarianteId, movimento.LoteId }
                    equals new { lote.ProdutoVarianteId, LoteId = lote.Id }
                orderby movimento.RegistradoEmUtc descending, movimento.Id descending
                select new MovimentoProdutoAcabadoSql(
                    movimento.Id,
                    movimento.Tipo,
                    movimento.Quantidade,
                    movimento.CondicaoOrigem,
                    movimento.CondicaoDestino,
                    movimento.QuantidadeContada,
                    movimento.SaldoAnterior.QuantidadeVendavel,
                    movimento.SaldoAnterior.QuantidadeDanificada,
                    movimento.SaldoAnterior.QuantidadeQuarentena,
                    movimento.SaldoPosterior.QuantidadeVendavel,
                    movimento.SaldoPosterior.QuantidadeDanificada,
                    movimento.SaldoPosterior.QuantidadeQuarentena,
                    movimento.Motivo,
                    movimento.Observacao,
                    movimento.ResponsavelId,
                    movimento.RegistradoEmUtc,
                    movimento.LoteId,
                    lote.Codigo,
                    movimento.MovimentoOriginalId,
                    dbContext.OrdensProducao.Where(o => o.MovimentoAcabadoId == movimento.Id)
                        .Select(o => (Guid?)o.Id).FirstOrDefault()))
            .Skip(skip)
            .Take(filtro.ItensPorPagina)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var itens = itensSql.Select(item => new MovimentoProdutoAcabadoConsulta(
                item.MovimentoId,
                item.Tipo,
                item.Quantidade,
                item.CondicaoOrigem,
                item.CondicaoDestino,
                item.QuantidadeContada,
                SaldoProdutoAcabadoSnapshot.Criar(
                    item.AnteriorVendavel,
                    item.AnteriorDanificada,
                    item.AnteriorQuarentena),
                SaldoProdutoAcabadoSnapshot.Criar(
                    item.PosteriorVendavel,
                    item.PosteriorDanificada,
                    item.PosteriorQuarentena),
                item.Motivo,
                item.Observacao,
                item.ResponsavelId,
                item.RegistradoEmUtc,
                item.LoteId,
                item.CodigoLote,
                item.MovimentoOriginalId,
                item.OrdemProducaoId))
            .ToArray();

        return new PaginaEstoque<MovimentoProdutoAcabadoConsulta>(
            itens,
            totalItens,
            filtro.Pagina,
            filtro.ItensPorPagina);
    }

    private static PosicaoLoteProdutoAcabadoConsulta MapearPosicao(
        PosicaoLoteSql posicao,
        bool varianteAtiva,
        DateOnly dataReferencia)
    {
        var (lote, saldo, versao, comprometido) = posicao;
        var valido = lote.EstaValidoEm(dataReferencia);
        var utilizavel = valido ? saldo?.QuantidadeVendavel ?? 0 : 0;
        var disponivel = varianteAtiva ? (int)Math.Max(0, utilizavel - comprometido) : 0;

        return new PosicaoLoteProdutoAcabadoConsulta(
            lote.Id,
            lote.Codigo,
            lote.DataFabricacao,
            lote.DataValidade,
            lote.OrigemValidade,
            lote.PrazoValidadeAplicadoDias,
            valido,
            saldo is not null,
            versao,
            saldo?.QuantidadeVendavel ?? 0,
            saldo?.QuantidadeDanificada ?? 0,
            saldo?.QuantidadeQuarentena ?? 0,
            saldo?.QuantidadeFisica ?? 0,
            utilizavel,
            checked((int)comprometido),
            disponivel);
    }

    private static ResumoEstoqueProdutoAcabadoConsulta MapearResumo(
        Guid produtoVarianteId,
        bool varianteAtiva,
        int? estoqueMinimo,
        IReadOnlyList<PosicaoLoteProdutoAcabadoConsulta> lotes)
    {
        var disponivel = lotes.Sum(lote => lote.SaldoDisponivel);
        var situacao = disponivel == 0
            ? SituacaoEstoqueProdutoAcabado.SemEstoque
            : estoqueMinimo is null
                ? SituacaoEstoqueProdutoAcabado.MinimoNaoConfigurado
                : disponivel <= estoqueMinimo.Value
                    ? SituacaoEstoqueProdutoAcabado.AbaixoOuNoMinimo
                    : SituacaoEstoqueProdutoAcabado.Normal;

        return new ResumoEstoqueProdutoAcabadoConsulta(
            produtoVarianteId,
            varianteAtiva,
            lotes.Sum(lote => lote.SaldoFisico),
            lotes.Sum(lote => lote.SaldoUtilizavel),
            lotes.Sum(lote => lote.SaldoReservado),
            disponivel,
            estoqueMinimo,
            estoqueMinimo is not null,
            situacao);
    }

    private sealed record PosicaoLoteSql(
        LoteProdutoAcabado Lote, SaldoLoteProdutoAcabado? Saldo, byte[]? Versao, long Comprometido);

    private sealed record MovimentoProdutoAcabadoSql(
        Guid MovimentoId,
        TipoMovimentoProdutoAcabado Tipo,
        int Quantidade,
        CondicaoEstoqueProdutoAcabado? CondicaoOrigem,
        CondicaoEstoqueProdutoAcabado? CondicaoDestino,
        int? QuantidadeContada,
        int AnteriorVendavel,
        int AnteriorDanificada,
        int AnteriorQuarentena,
        int PosteriorVendavel,
        int PosteriorDanificada,
        int PosteriorQuarentena,
        string Motivo,
        string? Observacao,
        Guid ResponsavelId,
        DateTimeOffset RegistradoEmUtc,
        Guid LoteId,
        string CodigoLote,
        Guid? MovimentoOriginalId,
        Guid? OrdemProducaoId);
}
