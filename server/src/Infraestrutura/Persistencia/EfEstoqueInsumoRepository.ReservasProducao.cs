using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class ReservasProducaoQuery
{
    internal static IQueryable<AlocacaoMateriaPrima> Ativas(this XaquiamuniDbContext db) =>
        db.AlocacoesMateriasPrimas.AsNoTracking().Where(a =>
            a.Necessidade.Ordem.Situacao == SituacaoOrdemProducao.EmAndamento && a.Reservada > a.Consumida);
}

public sealed partial class EfEstoqueInsumoRepository
{
    // O chamador controla a transação e deve gravar somente quando todos os materiais tiverem sido alocados.
    internal async Task<bool> PrepararReservaProducaoAsync(OrdemProducao ordem, CancellationToken ct)
    {
        ExigirTransacaoProducao();
        if (_dbContext.Entry(ordem).State != EntityState.Detached ||
            ordem.Situacao != SituacaoOrdemProducao.EmAndamento || ordem.Necessidades.Count == 0)
            throw new InvalidOperationException("Informe uma ordem nova em andamento, com necessidades válidas.");

        await AdquirirLockAsync($"production-order:{ordem.Id:D}", ct);
        await BloquearMateriaisProducaoAsync(ordem, ct);
        if (await _dbContext.OrdensProducao.AnyAsync(o => o.Id == ordem.Id, ct) ||
            !await _dbContext.ReceitasProducao.AnyAsync(r => r.Ativa && r.Variante.Ativa &&
                r.ProdutoVarianteId == ordem.ProdutoVarianteId && r.Versoes.Any(v => v.Id == ordem.ReceitaVersaoId && !r.Versoes.Any(a => a.Numero > v.Numero)), ct))
            return false;

        var data = _calendario.ObterDataAtual();
        var alocacoes = new List<AlocacaoMateriaPrima>();
        foreach (var necessidade in ordem.Necessidades)
        {
            var material = await _dbContext.InsumosEstoque.AsNoTracking()
                .SingleAsync(i => i.Id == necessidade.InsumoId, ct);
            if (!material.Ativo || material.UnidadeBase != necessidade.UnidadeBase) return false;

            var posicoes = await (
                from saldo in _dbContext.SaldosInsumo.AsNoTracking().Where(s => s.InsumoId == material.Id)
                join lote in _dbContext.LotesInsumo.AsNoTracking() on saldo.LoteId equals (Guid?)lote.Id into lotes
                from lote in lotes.DefaultIfEmpty()
                select new
                {
                    Saldo = saldo,
                    Lote = lote,
                    Reservado = _dbContext.Ativas().Where(a => a.SaldoId == saldo.Id)
                        .Sum(a => (decimal?)(a.Reservada - a.Consumida)) ?? 0m
                }).ToListAsync(ct);
            var reservadasLocais = _dbContext.ChangeTracker.Entries<AlocacaoMateriaPrima>()
                .Where(e => e.State == EntityState.Added && e.Entity.Necessidade.Ordem.Situacao == SituacaoOrdemProducao.EmAndamento)
                .ToLookup(e => e.Entity.SaldoId, e => e.Entity.Reservada - e.Entity.Consumida);
            var parcelas = AlocadorMateriaPrima.Planejar(
                QuantidadeEstoque.Criar(necessidade.Quantidade, necessidade.UnidadeBase), material.ControlaLote,
                posicoes.Select(p => new PosicaoParaReserva(p.Saldo.Id, p.Saldo.LoteId, p.Lote?.DataEntrada,
                    p.Lote?.DataValidade, p.Saldo.LoteId is null || p.Lote?.EstaElegivelEm(data) == true,
                    p.Saldo.QuantidadeFisica.ValorBase, p.Reservado + reservadasLocais[p.Saldo.Id].Sum())));
            if (parcelas is null) return false;
            var porId = posicoes.ToDictionary(p => p.Saldo.Id, p => p.Saldo);
            alocacoes.AddRange(parcelas.Select(p => new AlocacaoMateriaPrima(necessidade, porId[p.SaldoId], p.Quantidade)));
        }

        _dbContext.OrdensProducao.Add(ordem);
        _dbContext.AlocacoesMateriasPrimas.AddRange(alocacoes);
        return true;
    }

    internal async Task<bool> PrepararLiberacaoProducaoAsync(Guid ordemId, Guid autor, DateTimeOffset agora, CancellationToken ct, Guid? chave = null)
    {
        ExigirTransacaoProducao();
        await AdquirirLockAsync($"production-order:{ordemId:D}", ct);
        var ordem = await _dbContext.OrdensProducao.Include(o => o.Necessidades).SingleOrDefaultAsync(o => o.Id == ordemId, ct);
        if (ordem is null || ordem.Situacao == SituacaoOrdemProducao.Concluida) return false;
        if (ordem.Situacao == SituacaoOrdemProducao.Cancelada) return true;

        await BloquearMateriaisProducaoAsync(ordem, ct);
        if (await _dbContext.AlocacoesMateriasPrimas.AnyAsync(
            a => a.Necessidade.OrdemId == ordemId && a.Consumida > 0m, ct)) return false;
        ordem.Cancelar(autor, agora, chave);
        return true;
    }

    private async Task BloquearMateriaisProducaoAsync(OrdemProducao ordem, CancellationToken ct)
    {
        foreach (var id in ordem.Necessidades.Select(n => n.InsumoId).Distinct().Order())
            await AdquirirLockAsync($"stock-supply:{id:D}", ct);
    }

    private void ExigirTransacaoProducao()
    {
        if (_dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("A operação de reserva exige uma transação ReadCommitted do chamador.");
    }

    private async Task<bool> ViolaReservaProducaoAsync(SaldoInsumo? saldo, decimal novoSaldo, CancellationToken ct) =>
        saldo is not null && novoSaldo < (await _dbContext.Ativas().Where(a => a.SaldoId == saldo.Id)
            .SumAsync(a => (decimal?)(a.Reservada - a.Consumida), ct).ConfigureAwait(false) ?? 0m);
}
