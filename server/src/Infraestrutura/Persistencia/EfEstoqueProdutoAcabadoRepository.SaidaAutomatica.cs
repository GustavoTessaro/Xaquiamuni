using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IRegistrarSaidaAutomaticaProdutoAcabado
{
    public async Task<SaidaAutomaticaProdutoAcabadoResult> RegistrarSaidaAutomaticaAsync(
        SaidaAutomaticaProdutoAcabadoInput input, CancellationToken cancellationToken = default)
    {
        var chave = NormalizarChaveIdempotencia(input.ChaveIdempotencia);
        if (chave is null || input.VarianteId == Guid.Empty || input.ResponsavelId == Guid.Empty
            || input.Quantidade <= 0 || string.IsNullOrWhiteSpace(input.Motivo)
            || input.Motivo.Length > MovimentoProdutoAcabado.MotivoMaxLength)
            return new(OperacaoProdutoAcabadoStatus.OperacaoInvalida, [],
                Erro: "Informe variante, quantidade inteira positiva, motivo e chave de idempotência válidos.");

        var motivo = input.Motivo.Trim();
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"saida-fefo:v1:{chave.Length}:{chave}:{input.VarianteId:D}:{input.ResponsavelId:D}:{input.Quantidade}:{motivo}")));
        var prefixo = "saida-fefo-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chave))) + "-";
        await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await AdquirirLockAsync($"finished-entry:{input.ResponsavelId:D}:{chave}", cancellationToken);
            var primeiro = await _dbContext.MovimentosProdutoAcabado.AsNoTracking().SingleOrDefaultAsync(
                m => m.ResponsavelId == input.ResponsavelId && m.ChaveIdempotencia == chave, cancellationToken);
            if (primeiro is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(primeiro.FingerprintRequisicao, fingerprint))
                    return new(OperacaoProdutoAcabadoStatus.ConflitoIdempotencia, [],
                        Erro: "A chave de idempotência já foi usada com outros dados.");

                var seguintes = await _dbContext.MovimentosProdutoAcabado.AsNoTracking()
                    .Where(m => m.ResponsavelId == input.ResponsavelId
                        && m.ChaveIdempotencia.StartsWith(prefixo) && m.FingerprintRequisicao == fingerprint)
                    .OrderBy(m => m.ChaveIdempotencia).ToListAsync(cancellationToken);
                await transacao.CommitAsync(cancellationToken);
                return new(OperacaoProdutoAcabadoStatus.Sucesso, [primeiro, .. seguintes], Repetida: true);
            }

            await AdquirirLockVarianteAsync(input.VarianteId, cancellationToken);
            var variante = await _dbContext.ProdutoVariantes.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == input.VarianteId, cancellationToken);
            if (variante is null)
                return new(OperacaoProdutoAcabadoStatus.VarianteNaoEncontrada, []);
            if (!variante.Ativa)
                return new(OperacaoProdutoAcabadoStatus.VarianteInativa, [], Erro: "A variante está inativa.");

            // O mesmo instante define o dia civil, as reservas que comprometem saldo e a auditoria.
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken);
            var data = _calendario.ObterDataEm(agora);
            var comprometidas = _dbContext.ReservasProdutoAcabado.ComprometidasEm(agora)
                .Where(r => r.ProdutoVarianteId == input.VarianteId)
                .GroupBy(r => r.LoteId)
                .Select(g => new { LoteId = g.Key, Quantidade = g.Sum(r => (long)r.Quantidade) });
            var posicoes = await (
                from lote in _dbContext.LotesProdutoAcabado
                join saldo in _dbContext.SaldosProdutoAcabado
                    on new { lote.ProdutoVarianteId, LoteId = lote.Id } equals new { saldo.ProdutoVarianteId, saldo.LoteId }
                join reserva in comprometidas on lote.Id equals reserva.LoteId into reservas
                from reserva in reservas.DefaultIfEmpty()
                where lote.ProdutoVarianteId == input.VarianteId && lote.DataValidade >= data
                    && saldo.QuantidadeVendavel > ((long?)reserva.Quantidade ?? 0)
                orderby lote.DataValidade, lote.Codigo, lote.Id
                select new { Lote = lote, Saldo = saldo, Livre = saldo.QuantidadeVendavel - ((long?)reserva.Quantidade ?? 0) })
                .ToListAsync(cancellationToken);
            if (posicoes.Sum(p => p.Livre) < input.Quantidade)
                return new(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, [],
                    Erro: "Saldo disponível insuficiente para registrar a saída. Confira a posição antes de tentar novamente.");

            var restante = input.Quantidade;
            var movimentos = new List<MovimentoProdutoAcabado>();
            foreach (var posicao in posicoes)
            {
                var quantidade = (int)Math.Min(restante, posicao.Livre);
                var chaveMovimento = movimentos.Count == 0 ? chave : FormattableString.Invariant($"{prefixo}{movimentos.Count:D10}");
                movimentos.Add(posicao.Saldo.RegistrarSaidaManual(variante, posicao.Lote, quantidade, data,
                    input.ResponsavelId, motivo, agora, chaveMovimento, fingerprint));
                restante -= quantidade;
                if (restante == 0) break;
            }

            _dbContext.MovimentosProdutoAcabado.AddRange(movimentos);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return new(OperacaoProdutoAcabadoStatus.Sucesso, movimentos);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(OperacaoProdutoAcabadoStatus.ConflitoConcorrencia, [],
                Erro: "O estoque mudou durante a saída. Repita com a mesma chave.");
        }
        catch (DbUpdateException erro) when (IsUniqueViolation(erro))
        {
            return new(OperacaoProdutoAcabadoStatus.ConflitoIdempotencia, [],
                Erro: "A chave de idempotência já foi usada por outra operação.");
        }
        catch (SqlException erro) when (erro.Number is 1205 or 1222 or 51000)
        {
            return new(OperacaoProdutoAcabadoStatus.ConflitoConcorrencia, [],
                Erro: "A saída conflitou com outra alteração de estoque. Repita com a mesma chave.");
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }
}
