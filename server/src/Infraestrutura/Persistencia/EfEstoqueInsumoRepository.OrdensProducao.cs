using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueInsumoRepository : IGerenciarOrdensProducao
{
    public async Task<IReadOnlyList<ResumoOrdemProducao>> ListarAsync(SituacaoOrdemProducao? situacao,
        Guid? produtoId, Guid? receitaId, CancellationToken ct) =>
        await Resumos(null, situacao, produtoId, receitaId).ToListAsync(ct);

    public async Task<DetalheOrdemProducao?> ConsultarAsync(Guid id, CancellationToken ct)
    {
        var resumo = await Resumos(id, null, null, null).SingleOrDefaultAsync(ct);
        if (resumo is null) return null;

        var data = _calendario.ObterDataAtual();
        var materiais = await _dbContext.Set<NecessidadeOrdemProducao>().AsNoTracking()
            .Where(n => n.OrdemId == id).OrderBy(n => n.NomeInsumo).ThenBy(n => n.Id)
            .Select(n => new MaterialOrdemProducao(n.InsumoId, n.NomeInsumo, n.UnidadeBase.ToString(), n.Quantidade,
                n.Ordem.Situacao == SituacaoOrdemProducao.EmAndamento
                    ? _dbContext.Ativas().Where(a => a.NecessidadeId == n.Id).Sum(a => (decimal?)(a.Reservada - a.Consumida)) ?? 0m
                    : 0m,
                _dbContext.Ativas().Any(a => a.NecessidadeId == n.Id && a.Saldo.LoteId != null &&
                    _dbContext.LotesInsumo.Any(l => l.Id == a.Saldo.LoteId && (l.Bloqueado || l.DataValidade < data)))))
            .ToListAsync(ct);
        var varianteId = await _dbContext.OrdensProducao.Where(o => o.Id == id)
            .Select(o => o.ProdutoVarianteId).SingleAsync(ct);
        var conclusao = resumo.Situacao == nameof(SituacaoOrdemProducao.Concluida)
            ? await ConsultarConclusaoAsync(id, ct)
            : null;
        return new(resumo, materiais, varianteId, conclusao);
    }

    public Task<Guid?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct) =>
        _dbContext.OrdensProducao.Where(o => o.IniciadaPorId == autor && o.ChaveInicio == chave)
            .Select(o => (Guid?)o.Id).SingleOrDefaultAsync(ct);

    public async Task<OperacaoProducaoResult> PreverAsync(Guid receitaId, int quantidade, CancellationToken ct)
    {
        try
        {
            var receita = await CarregarReceitaAsync(receitaId, false, ct);
            if (receita is null) return new(OperacaoProducaoStatus.Ausente);
            ValidarReceitaProducao(receita);
            return new(OperacaoProducaoStatus.Sucesso, Previa: await CalcularPreviaAsync(receita, quantidade, ct));
        }
        catch (ArgumentException erro)
        {
            return new(OperacaoProducaoStatus.Invalida, Erro: erro.Message);
        }
    }

    public Task<OperacaoProducaoResult> IniciarAsync(InicioProducaoInput input, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarProducaoAsync(async () =>
        {
            await AdquirirLockAsync($"production-start-key:{autor:D}:{chave:D}", ct);
            var impressao = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"{input.ReceitaId:D}|{input.ReceitaVersaoId:D}|{input.Quantidade}"))));
            var anterior = await _dbContext.OrdensProducao.AsNoTracking()
                .SingleOrDefaultAsync(o => o.IniciadaPorId == autor && o.ChaveInicio == chave, ct);
            if (anterior is not null)
                return anterior.ImpressaoInicio == impressao
                    ? new(OperacaoProducaoStatus.Repetida, anterior.Id)
                    : ConflitoProducao("A chave já foi usada com outros dados.");

            var receita = await CarregarReceitaAsync(input.ReceitaId, true, ct);
            if (receita is null) return new(OperacaoProducaoStatus.Ausente);
            ValidarReceitaProducao(receita);
            if (receita.Atual.Id != input.ReceitaVersaoId)
                return ConflitoProducao("A receita mudou. Consulte uma nova prévia.");

            var ordem = OrdemProducao.Criar(receita, input.Quantidade, autor, _timeProvider.GetUtcNow());
            ordem.RegistrarTentativaInicio(chave, impressao);
            if (!await PrepararReservaProducaoAsync(ordem, ct))
            {
                var previa = await CalcularPreviaAsync(receita, input.Quantidade, ct);
                return new(OperacaoProducaoStatus.Conflito, Previa: previa,
                    Erro: "O estoque livre é insuficiente ou a receita mudou. Confira os materiais e atualize a prévia.",
                    RejeicaoDefinitiva: true);
            }
            return new(OperacaoProducaoStatus.Sucesso, ordem.Id);
        }, ct);

    public Task<OperacaoProducaoResult> CancelarAsync(Guid id, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarProducaoAsync(async () =>
        {
            await AdquirirLockAsync($"production-cancel-key:{autor:D}:{chave:D}", ct);
            await AdquirirLockAsync($"production-order:{id:D}", ct);
            var anterior = await _dbContext.OrdensProducao.AsNoTracking()
                .SingleOrDefaultAsync(o => o.EncerradaPorId == autor && o.ChaveCancelamento == chave, ct);
            if (anterior is not null)
                return anterior.Id == id ? new(OperacaoProducaoStatus.Repetida, id)
                    : ConflitoProducao("A chave já foi usada para outra ordem.");
            var situacao = await _dbContext.OrdensProducao.Where(o => o.Id == id)
                .Select(o => (SituacaoOrdemProducao?)o.Situacao).SingleOrDefaultAsync(ct);
            if (situacao is null) return new(OperacaoProducaoStatus.Ausente);
            if (situacao != SituacaoOrdemProducao.EmAndamento ||
                !await PrepararLiberacaoProducaoAsync(id, autor, _timeProvider.GetUtcNow(), ct, chave))
                return ConflitoProducao("Só é possível cancelar uma ordem em andamento sem consumo de materiais.");
            return new(OperacaoProducaoStatus.Sucesso, id);
        }, ct);

    private IQueryable<ResumoOrdemProducao> Resumos(Guid? id, SituacaoOrdemProducao? situacao, Guid? produtoId, Guid? receitaId) =>
        from ordem in _dbContext.OrdensProducao.AsNoTracking()
        join variante in _dbContext.ProdutoVariantes on ordem.ProdutoVarianteId equals variante.Id
        join versao in _dbContext.Set<ReceitaProducaoVersao>() on ordem.ReceitaVersaoId equals versao.Id
        join receita in _dbContext.ReceitasProducao on EF.Property<Guid>(versao, "ReceitaId") equals receita.Id
        where (!id.HasValue || ordem.Id == id) && (!situacao.HasValue || ordem.Situacao == situacao) &&
            (!produtoId.HasValue || variante.ProdutoId == produtoId) && (!receitaId.HasValue || receita.Id == receitaId)
        orderby ordem.IniciadaEmUtc descending, ordem.Id descending
        select new ResumoOrdemProducao(ordem.Id, variante.ProdutoId, ordem.NomeProduto ?? variante.Produto.Nome,
            ordem.NomeVariante ?? variante.Identificacao, receita.Id, ordem.NomeReceita ?? receita.Nome, versao.Numero, ordem.QuantidadePlanejada,
            ordem.Situacao.ToString(), ordem.IniciadaEmUtc, ordem.EncerradaEmUtc, ordem.Versao, ordem.NomeProduto == null || ordem.NomeVariante == null);

    private async Task<ReceitaProducao?> CarregarReceitaAsync(Guid id, bool bloquear, CancellationToken ct)
    {
        IQueryable<ReceitaProducao> query = bloquear
            ? _dbContext.ReceitasProducao.FromSqlInterpolated($"SELECT * FROM [ReceitasProducao] WITH (HOLDLOCK) WHERE [Id] = {id}")
            : _dbContext.ReceitasProducao;
        var receita = await query.AsNoTracking().Include(r => r.Variante).ThenInclude(v => v.Produto)
            .Include(r => r.Versoes.OrderByDescending(v => v.Numero).Take(1)).ThenInclude(v => v.Itens)
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (bloquear && receita is not null)
        {
            var ativa = await _dbContext.ProdutoVariantes
                .FromSqlInterpolated($"SELECT * FROM [ProdutoVariantes] WITH (HOLDLOCK) WHERE [Id] = {receita.ProdutoVarianteId}")
                .AsNoTracking().Select(v => v.Ativa).SingleAsync(ct);
            if (!ativa) throw new ArgumentException("A variante da receita está inativa.");
        }
        return receita;
    }

    private async Task<PreviaProducao> CalcularPreviaAsync(ReceitaProducao receita, int quantidade, CancellationToken ct)
    {
        var versao = receita.Atual;
        var necessidades = versao.CalcularNecessidades(quantidade);
        var snapshots = versao.Itens.ToDictionary(i => i.InsumoId);
        var consulta = new EfEstoqueInsumoConsultaRepository(_dbContext);
        var saldos = await consulta.ConsultarResumosAsync(necessidades.Select(n => n.InsumoId).ToArray(), _calendario.ObterDataAtual(), ct);
        var materiais = necessidades.Select(n =>
        {
            var snapshot = snapshots[n.InsumoId];
            if (!saldos.TryGetValue(n.InsumoId, out var saldo) || !saldo.Ativo || saldo.UnidadeBase != snapshot.UnidadeBase)
                throw new ArgumentException($"O material {snapshot.NomeInsumo} está inativo ou foi alterado. Revise a receita.");
            var necessario = n.Quantidade.ValorBase;
            return new MaterialProducao(n.InsumoId, snapshot.NomeInsumo, snapshot.UnidadeBase.ToString(),
                necessario, saldo.SaldoUtilizavel, saldo.SaldoReservado, saldo.SaldoDisponivel,
                Math.Max(0m, necessario - saldo.SaldoDisponivel));
        }).OrderBy(m => m.Nome).ThenBy(m => m.InsumoId).ToArray();
        return new(versao.Id, versao.Numero, materiais);
    }

    private async Task<OperacaoProducaoResult> ExecutarProducaoAsync(Func<Task<OperacaoProducaoResult>> executar, CancellationToken ct)
    {
        await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var resultado = await executar();
            if (resultado.Status is OperacaoProducaoStatus.Sucesso or OperacaoProducaoStatus.Repetida)
            {
                await _dbContext.SaveChangesAsync(ct);
                await transacao.CommitAsync(ct);
            }
            return resultado;
        }
        catch (ArgumentException erro)
        {
            return new(OperacaoProducaoStatus.Invalida, Erro: erro.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflitoProducao("A ordem ou o estoque mudou. Confira a situação antes de reenviar.", definitiva: false);
        }
        catch (SqlException erro) when (erro.Number is 1205 or 1222 or 51000)
        {
            return ConflitoProducao("Outra operação alterou o estoque. Confira a situação antes de reenviar.", definitiva: false);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 1205 or 1222 or 2601 or 2627 })
        {
            return ConflitoProducao("Houve uma operação concorrente. Confira a situação antes de reenviar.", definitiva: false);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static void ValidarReceitaProducao(ReceitaProducao receita)
    {
        if (!receita.Ativa || !receita.Variante.Ativa)
            throw new ArgumentException("A receita ou a variante está inativa.");
    }

    private static OperacaoProducaoResult ConflitoProducao(string erro, bool definitiva = true) =>
        new(OperacaoProducaoStatus.Conflito, Erro: erro, RejeicaoDefinitiva: definitiva);
}
