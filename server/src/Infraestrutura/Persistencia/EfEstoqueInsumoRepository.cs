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
            insumo.AplicarEntrada(saldo, movement);
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

    private static async Task ReverterSePossivelAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
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
        if (!Enum.IsDefined(input.Tipo))
        {
            return "Tipo de entrada inválido.";
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
