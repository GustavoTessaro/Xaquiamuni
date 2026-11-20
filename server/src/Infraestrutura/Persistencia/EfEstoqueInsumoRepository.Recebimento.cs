using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueInsumoRepository : IReceberCompraMateriaPrima
{
    public async Task<ReceberCompraMateriaPrimaResult> ReceberAsync(Guid id, Guid autor, Guid chave,
        ReceberCompraMateriaPrimaInput input, CancellationToken ct)
    {
        if (id == Guid.Empty || autor == Guid.Empty || chave == Guid.Empty || input.Versao == Guid.Empty)
            return new(CompraMateriaPrimaStatus.Invalida, Erro: "Informe compra, responsável, versão e chave válidos.");

        if (input.SemControleValidade && input.Validades is { Count: > 0 })
            return new(CompraMateriaPrimaStatus.Invalida, Erro: "O recebimento sem controle de validade nao recebe datas de validade.");
        if (input.Validades is { Count: > 1500 })
            return new(CompraMateriaPrimaStatus.Invalida, Erro: "A lista de validades excede o limite da compra.");

        var validades = new Dictionary<Guid, DateOnly>();
        foreach (var item in input.Validades ?? [])
        {
            if (item is null || item.ComponenteId == Guid.Empty || item.DataValidade == default ||
                !validades.TryAdd(item.ComponenteId, item.DataValidade))
                return new(CompraMateriaPrimaStatus.Invalida, Erro: "Informe no máximo uma validade por componente.");
        }

        var dadosValidade = string.Join('|', validades.OrderBy(v => v.Key)
            .Select(v => FormattableString.Invariant($"{v.Key:D}:{v.Value:yyyy-MM-dd}")));
        var impressao = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}|{input.Versao:D}|{dadosValidade}{(input.SemControleValidade ? "|sem-controle-validade" : "")}")));

        await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AdquirirLockAsync($"supply-receipt-key:{autor:D}:{chave:D}", ct);
            await AdquirirLockAsync($"supply-receipt:{id:D}", ct);

            var anterior = await _dbContext.ComprasMateriasPrimas.AsNoTracking()
                .SingleOrDefaultAsync(c => c.RecebidaPorId == autor && c.ChaveRecebimento == chave, ct);
            if (anterior is not null)
            {
                if (anterior.Id != id || anterior.ImpressaoRecebimento != impressao)
                    return ConflitoRecebimento("A chave de idempotência já foi usada com outros dados.");
                var entradasAnteriores = await _dbContext.EntradasRecebimentoCompra.AsNoTracking()
                    .Where(e => e.CompraId == id).OrderBy(e => e.Ordem).ToArrayAsync(ct);
                await transacao.CommitAsync(ct);
                return new(CompraMateriaPrimaStatus.Repetida, anterior, entradasAnteriores);
            }

            var compra = await _dbContext.ComprasMateriasPrimas.Include(c => c.Linhas).ThenInclude(l => l.Componentes)
                .SingleOrDefaultAsync(c => c.Id == id, ct);
            if (compra is null) return new(CompraMateriaPrimaStatus.Ausente);
            if (compra.Situacao != SituacaoCompraMateriaPrima.Rascunho)
                return ConflitoRecebimento("Apenas compras em rascunho podem ser recebidas.");
            if (compra.Versao != input.Versao)
                return ConflitoRecebimento("A compra foi alterada. Atualize o rascunho antes de receber.");

            var itens = compra.Linhas.OrderBy(l => l.Ordem)
                .SelectMany(l => l.Componentes.OrderBy(c => c.Ordem).Select(c => (Linha: l, Componente: c))).ToArray();
            var componentes = itens.Select(i => i.Componente.Id).ToHashSet();
            if (validades.Keys.Any(componente => !componentes.Contains(componente)))
                return new(CompraMateriaPrimaStatus.Invalida, Erro: "A validade informada não pertence a esta compra.");

            var insumoIds = itens.Select(i => i.Componente.InsumoId).Distinct().Order().ToArray();
            foreach (var insumoId in insumoIds)
                await AdquirirLockAsync($"stock-supply:{insumoId:D}", ct);

            var insumos = await _dbContext.InsumosEstoque.Include(i => i.FormasCompra)
                .Where(i => insumoIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var agora = _timeProvider.GetUtcNow().ToUniversalTime();
            var data = _calendario.ObterDataEm(agora);
            compra.ConfirmarRecebimento(autor, chave, impressao, agora);
            var entradas = new List<EntradaRecebimentoCompra>(itens.Length);
            foreach (var (linha, componente) in itens)
            {
                if (!insumos.TryGetValue(componente.InsumoId, out var insumo) || !insumo.Ativo)
                    return ConflitoRecebimento($"O material {componente.NomeInsumo} está inativo ou indisponível. Revise o rascunho.");
                if (componente.ApresentacaoId is Guid formaId && !insumo.FormasCompra.Any(f => f.Id == formaId && f.Ativa))
                    return ConflitoRecebimento($"A apresentação de {componente.NomeInsumo} está indisponível. Revise o rascunho.");

                var controlaLote = insumo.ControlaLote;
                DateOnly? validade = validades.TryGetValue(componente.Id, out var valor) ? valor : null;
                var chaveMovimento = $"recebimento:{Guid.NewGuid():N}";
                var entrada = new RegistrarEntradaInsumoInput(insumo.Id, autor, TipoMovimentoInsumo.Aquisicao,
                    componente.Quantidade, componente.ApresentacaoId is null ? componente.UnidadeBase : null,
                    componente.ApresentacaoId, null, controlaLote ? $"Compra {compra.Id:N}/{componente.Id:N}" : null,
                    controlaLote ? data : null, validade, controlaLote ? compra.NomeFornecedor : null,
                    false, linha.Kit is null ? linha.ValorTotal : null, chaveMovimento, SemControleValidade: input.SemControleValidade);

                var resultado = await AplicarEntradaAsync(entrada, chaveMovimento, CalcularFingerprint(entrada),
                    ct, componente, data, agora, insumo);
                if (!resultado.Sucesso)
                {
                    var erro = $"{componente.NomeInsumo}: {resultado.Erro}";
                    return resultado.Status == RegistrarEntradaInsumoStatus.ConflitoConcorrencia
                        ? ConflitoRecebimento(erro) : new(CompraMateriaPrimaStatus.Invalida, Erro: erro);
                }
                var vinculo = new EntradaRecebimentoCompra(compra, linha, componente, resultado.Movimento!, entradas.Count);
                entradas.Add(vinculo);
                _dbContext.EntradasRecebimentoCompra.Add(vinculo);
                // O próximo componente pode usar a mesma posição agregada. A transação continua aberta.
                await _dbContext.SaveChangesAsync(ct);
            }

            await transacao.CommitAsync(ct);
            return new(CompraMateriaPrimaStatus.Sucesso, compra, entradas);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflitoRecebimento("O rascunho ou estoque mudou. Atualize os dados antes de tentar novamente.");
        }
        catch (SqlException erro) when (erro.Number is 1205 or 1222 or 51000)
        {
            return ConflitoRecebimento("Outra operação está alterando o estoque. Tente novamente com a mesma chave.");
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return ConflitoRecebimento("Outra operação está alterando o estoque. Tente novamente com a mesma chave.");
        }
        catch (ArgumentException erro)
        {
            return new(CompraMateriaPrimaStatus.Invalida, Erro: erro.Message);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static ReceberCompraMateriaPrimaResult ConflitoRecebimento(string erro) =>
        new(CompraMateriaPrimaStatus.Conflito, Erro: erro);
}
