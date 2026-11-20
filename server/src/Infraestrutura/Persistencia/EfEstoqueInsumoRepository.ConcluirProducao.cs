using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueInsumoRepository : IConcluirOrdemProducao
{
    public Task<OperacaoProducaoResult> ConcluirAsync(Guid id, Guid autor, Guid chave,
        ConcluirProducaoInput input, CancellationToken ct) =>
        ExecutarProducaoAsync(async () =>
        {
            if (input.Versao == Guid.Empty || input.QuantidadeProduzida <= 0 || input.Consumos is not { Count: >= 1 and <= 100 })
                throw new ArgumentException("Informe a versão, a quantidade produzida positiva e os consumos.");
            var consumos = LerConsumos(input.Consumos);
            var itens = string.Join('|', consumos.OrderBy(c => c.Key).Select(c =>
                FormattableString.Invariant($"{c.Key:D}:{c.Value.UnidadeBase}:{c.Value.ValorBase:0.######}")));
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
                $"{id:D}|{input.Versao:D}|{input.QuantidadeProduzida}|{input.DataFabricacao:yyyy-MM-dd}|{input.DataValidade:yyyy-MM-dd}|{itens}{(input.SemControleValidade ? "|sem-controle-validade" : "")}")));
            var impressao = Convert.ToHexString(digest);

            await AdquirirLockAsync($"production-finish-key:{autor:D}:{chave:D}", ct);
            await AdquirirLockAsync($"production-order:{id:D}", ct);
            var anterior = await _dbContext.OrdensProducao.AsNoTracking()
                .SingleOrDefaultAsync(o => o.EncerradaPorId == autor && o.ChaveConclusao == chave, ct);
            if (anterior is not null)
                return anterior.Id == id && anterior.ImpressaoConclusao == impressao
                    ? new(OperacaoProducaoStatus.Repetida, id)
                    : ConflitoProducao("A chave de idempotência já foi usada com outros dados.");

            var ordem = await _dbContext.OrdensProducao.Include(o => o.Necessidades).SingleOrDefaultAsync(o => o.Id == id, ct);
            if (ordem is null) return new(OperacaoProducaoStatus.Ausente);
            if (ordem.Situacao != SituacaoOrdemProducao.EmAndamento)
                return ConflitoProducao("Só é possível concluir uma ordem em andamento.");
            if (ordem.Versao != input.Versao)
                return ConflitoProducao("A ordem mudou. Consulte a versão atual.");
            if (ordem.Necessidades.Count == 0 || ordem.Necessidades.Count != consumos.Count ||
                ordem.Necessidades.Any(n => !consumos.ContainsKey(n.InsumoId)))
                throw new ArgumentException("Informe o consumo de exatamente todos os materiais da ordem.");

            // Ordem global: chave da operação, ordem, insumos ordenados por ID e variante acabada.
            await BloquearMateriaisProducaoAsync(ordem, ct);
            await AdquirirLockAsync($"finished-variant:{ordem.ProdutoVarianteId:D}", ct);
            var agora = _timeProvider.GetUtcNow().ToUniversalTime();
            if (agora < ordem.IniciadaEmUtc) return ConflitoProducao("O relógio está anterior ao início da ordem.", definitiva: false);
            var data = _calendario.ObterDataEm(agora);
            var alocacoes = await _dbContext.AlocacoesMateriasPrimas.Include(a => a.Saldo)
                .Include(a => a.Necessidade).Where(a => a.Necessidade.OrdemId == id).ToListAsync(ct);
            var lotesIds = alocacoes.Select(a => a.Saldo.LoteId).OfType<Guid>().Distinct().ToArray();
            var lotes = await _dbContext.LotesInsumo.AsNoTracking().Where(l => lotesIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);
            var ids = consumos.Keys.ToArray();
            var insumos = await _dbContext.InsumosEstoque.AsNoTracking()
                .Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var saldos = alocacoes.Select(a => a.SaldoId).Distinct().ToArray();
            var compromissos = await _dbContext.Ativas().Where(a => saldos.Contains(a.SaldoId))
                .GroupBy(a => a.SaldoId).Select(g => new { Saldo = g.Key, Reservado = g.Sum(a => a.Reservada - a.Consumida) })
                .ToDictionaryAsync(g => g.Saldo, g => g.Reservado, ct);

            foreach (var necessidade in ordem.Necessidades)
            {
                var consumo = consumos[necessidade.InsumoId];
                if (consumo.UnidadeBase != necessidade.UnidadeBase)
                    throw new ArgumentException($"Use a unidade-base de {necessidade.NomeInsumo}.");
                var parcelas = alocacoes.Where(a => a.NecessidadeId == necessidade.Id).ToArray();
                if (parcelas.Sum(a => a.Reservada) != necessidade.Quantidade || parcelas.Any(a => a.Consumida != 0m || a.MovimentoConsumoId.HasValue))
                    return ConflitoProducao($"As reservas de {necessidade.NomeInsumo} estão inconsistentes.");
                if (consumo.ValorBase > necessidade.Quantidade)
                    throw new ArgumentException($"O consumo de {necessidade.NomeInsumo} excede a reserva.");
                if (!insumos.TryGetValue(necessidade.InsumoId, out var insumo) || !insumo.Ativo ||
                    insumo.UnidadeBase != necessidade.UnidadeBase)
                    return ConflitoProducao($"O material {necessidade.NomeInsumo} está inelegível.");

                var restante = consumo.ValorBase;
                var ordenadas = parcelas.Select(a => (Alocacao: a,
                        Lote: a.Saldo.LoteId is Guid loteId ? lotes.GetValueOrDefault(loteId) : null))
                    .OrderBy(p => !p.Lote?.DataValidade.HasValue ?? true).ThenBy(p => p.Lote?.DataValidade)
                    .ThenBy(p => p.Lote?.DataEntrada).ThenBy(p => p.Lote?.Id).ThenBy(p => p.Alocacao.SaldoId);
                foreach (var (alocacao, lote) in ordenadas)
                {
                    if (restante == 0m) break;
                    var saldo = alocacao.Saldo;
                    if (saldo.InsumoId != insumo.Id || insumo.ControlaLote != saldo.LoteId.HasValue ||
                        (saldo.LoteId.HasValue && (lote is null || lote.InsumoId != insumo.Id || !lote.EstaElegivelEm(data))) ||
                        saldo.QuantidadeFisica.ValorBase < compromissos.GetValueOrDefault(saldo.Id))
                        return ConflitoProducao($"Uma posição reservada de {necessidade.NomeInsumo} está inelegível ou sem saldo.");
                    var quantidade = QuantidadeEstoque.Criar(Math.Min(restante, alocacao.Reservada), necessidade.UnidadeBase);
                    var movimento = MovimentoInsumo.CriarSaida(insumo, lote, autor, TipoMovimentoInsumo.SaidaManual,
                        quantidade.ValorBase, quantidade.UnidadeBase, quantidade, saldo.QuantidadeFisica,
                        "Consumo de produção", $"Ordem de produção {id:D}", agora, $"producao:{Guid.NewGuid():N}", digest);
                    alocacao.RegistrarConsumo(movimento);
                    insumo.AplicarMovimento(saldo, movimento);
                    _dbContext.MovimentosInsumo.Add(movimento);
                    restante -= quantidade.ValorBase;
                }
            }

            await _dbContext.SaveChangesAsync(ct);
            var acabados = new EfEstoqueProdutoAcabadoRepository(_dbContext, _timeProvider, _calendario);
            var entrada = await acabados.AplicarEntradaProducaoAsync(ordem.ProdutoVarianteId, input.QuantidadeProduzida,
                input.DataFabricacao, input.DataValidade, input.SemControleValidade, data, agora, autor, id, digest, ct);
            ordem.Concluir(input.QuantidadeProduzida, entrada.Id, autor, chave, impressao, agora);
            return new(OperacaoProducaoStatus.Sucesso, id);
        }, ct);

    public async Task<ConclusaoProducao> ConsultarConclusaoAsync(Guid id, CancellationToken ct)
    {
        var dados = await (from ordem in _dbContext.OrdensProducao.AsNoTracking()
            join movimento in _dbContext.MovimentosProdutoAcabado on ordem.MovimentoAcabadoId equals (Guid?)movimento.Id
            join lote in _dbContext.LotesProdutoAcabado on movimento.LoteId equals lote.Id
            where ordem.Id == id && ordem.Situacao == SituacaoOrdemProducao.Concluida
            select new { Ordem = ordem, Movimento = movimento.Id, Lote = lote }).SingleAsync(ct);
        var consumos = await _dbContext.Set<NecessidadeOrdemProducao>().AsNoTracking().Where(n => n.OrdemId == id)
            .OrderBy(n => n.NomeInsumo).ThenBy(n => n.InsumoId)
            .Select(n => new ConsumoProducaoResultado(n.InsumoId, n.NomeInsumo, n.UnidadeBase.ToString(),
                _dbContext.AlocacoesMateriasPrimas.Where(a => a.NecessidadeId == n.Id).Sum(a => a.Consumida),
                _dbContext.AlocacoesMateriasPrimas.Where(a => a.NecessidadeId == n.Id).Sum(a => a.Reservada - a.Consumida)))
            .ToListAsync(ct);
        return new(id, dados.Ordem.Situacao.ToString(), dados.Ordem.Versao, dados.Ordem.QuantidadeProduzida!.Value,
            dados.Lote.Id, dados.Lote.Codigo, dados.Movimento, dados.Lote.DataFabricacao, dados.Lote.DataValidade,
            dados.Ordem.EncerradaPorId!.Value, dados.Ordem.EncerradaEmUtc!.Value, consumos);
    }

    private static Dictionary<Guid, QuantidadeEstoque> LerConsumos(IReadOnlyList<ConsumoProducaoInput> itens)
    {
        var consumos = new Dictionary<Guid, QuantidadeEstoque>();
        foreach (var item in itens)
        {
            var partes = item?.Quantidade?.Split('.');
            if (item is null || item.InsumoId == Guid.Empty ||
                partes is not { Length: 1 or 2 } || partes[0].Length is < 1 or > 12 ||
                (partes.Length == 2 && partes[1].Length is < 1 or > 6) || partes.Any(p => !p.All(char.IsAsciiDigit)) ||
                !decimal.TryParse(item.Quantidade, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantidade) ||
                !Enum.TryParse<UnidadeFisicaEstoque>(item.UnidadeBase, out var unidade) || unidade.ToString() != item.UnidadeBase ||
                unidade is not (UnidadeFisicaEstoque.Grama or UnidadeFisicaEstoque.Mililitro or UnidadeFisicaEstoque.Unidade))
                throw new ArgumentException("Informe materiais e quantidades decimais válidas em unidade-base.");
            if (!consumos.TryAdd(item.InsumoId, QuantidadeEstoque.Criar(quantidade, unidade).ExigirPositiva()))
                throw new ArgumentException("Informe cada material uma única vez.");
        }
        return consumos;
    }
}
