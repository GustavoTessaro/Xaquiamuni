using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfReceitaProducaoRepository(XaquiamuniDbContext db, TimeProvider relogio) : IGerenciarReceitasProducao
{
    public async Task<IReadOnlyList<ReceitaProducao>> ListarAsync(CancellationToken ct) =>
        await db.ReceitasProducao.AsNoTracking().Include(r => r.Variante).ThenInclude(v => v.Produto)
            .Include(r => r.Versoes.OrderByDescending(v => v.Numero).Take(1))
            .OrderBy(r => r.Nome).ThenBy(r => r.Id).ToListAsync(ct);

    public Task<ReceitaProducao?> ObterAsync(Guid id, CancellationToken ct) =>
        Detalhes().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

    public async Task<ReceitaResult> SalvarAsync(Guid? id, ReceitaInput input, Guid autor, CancellationToken ct)
    {
        var receita = id.HasValue ? await Detalhes().SingleOrDefaultAsync(r => r.Id == id, ct) : null;
        if (id.HasValue && receita is null) return new(ReceitaStatus.Ausente);
        if (receita is not null && receita.Token != input.Token)
            return Conflito("A receita foi alterada. Atualize o detalhe antes de salvar.");
        if (receita is not null && receita.ProdutoVarianteId != input.ProdutoVarianteId)
            return new(ReceitaStatus.Invalida, Erro: "A variante é fixa. Cadastre outra receita para outra variante.");

        try
        {
            if (input.Itens is not { Count: >= 1 and <= 100 } || input.Itens.Any(i => i is null))
                throw new ArgumentException("Informe de 1 a 100 materiais.");
            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            foreach (var material in input.Itens.Select(item => item.InsumoId).Distinct().Order())
                await EfExcluirInsumo.BloquearVinculoAsync(db, material, ct);
            var agora = relogio.GetUtcNow();
            if (receita is null)
            {
                var variante = await db.ProdutoVariantes.Include(v => v.Produto)
                    .SingleOrDefaultAsync(v => v.Id == input.ProdutoVarianteId, ct);
                if (variante is null || !variante.Ativa)
                    throw new ArgumentException("Selecione uma variante ativa.");
                var itens = await PrepararItensAsync(input.Itens, null, ct);
                receita = ReceitaProducao.Criar(variante, input.Nome, input.Rendimento, itens, autor, agora);
                db.ReceitasProducao.Add(receita);
            }
            else
            {
                var itens = await PrepararItensAsync(input.Itens, receita.Atual, ct);
                var nova = receita.Editar(input.Nome, input.Rendimento, itens, autor, agora);
                if (nova is not null) db.Add(nova);
            }
            var resultado = await GravarAsync(receita, ct);
            if (resultado.Status == ReceitaStatus.Sucesso) await transacao.CommitAsync(ct);
            return resultado;
        }
        catch (ArgumentException erro)
        {
            return new(ReceitaStatus.Invalida, Erro: erro.Message);
        }
        catch (Exception erro) when (EfFornecedorRepository.ErroLock(erro))
        {
            return Conflito("O cadastro foi alterado durante a operação. Atualize os dados antes de salvar.");
        }
    }

    public async Task<ReceitaResult> AlterarStatusAsync(Guid id, bool ativa, Guid token, Guid autor, CancellationToken ct)
    {
        var receita = await Detalhes().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (receita is null) return new(ReceitaStatus.Ausente);
        if (receita.Token != token) return Conflito("A receita foi alterada. Atualize o detalhe antes de continuar.");
        try
        {
            if (ativa && !receita.Ativa)
            {
                var ids = receita.Atual.Itens.Select(i => i.InsumoId).ToArray();
                if (await db.InsumosEstoque.CountAsync(i => ids.Contains(i.Id) && i.Ativo, ct) != ids.Length)
                    throw new ArgumentException("Substitua ou reative os materiais inativos antes de reativar a receita.");
            }
            receita.DefinirAtiva(ativa, autor, relogio.GetUtcNow());
            return await GravarAsync(receita, ct);
        }
        catch (ArgumentException erro)
        {
            return new(ReceitaStatus.Invalida, Erro: erro.Message);
        }
    }

    private IQueryable<ReceitaProducao> Detalhes() => db.ReceitasProducao.Include(r => r.Variante).ThenInclude(v => v.Produto)
        .Include(r => r.Versoes).ThenInclude(v => v.Itens);

    private async Task<ItemReceitaProducao[]> PrepararItensAsync(IReadOnlyList<ItemReceitaInput> itens,
        ReceitaProducaoVersao? anterior, CancellationToken ct)
    {
        var ids = itens.Select(i => i.InsumoId).Distinct().ToArray();
        var materiais = await db.InsumosEstoque.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var antigos = anterior?.Itens.ToDictionary(i => i.InsumoId) ?? [];
        return itens.Select(item =>
        {
            var texto = item.Quantidade?.Split('.');
            if (texto is not { Length: 1 or 2 } || texto[0].Length is < 1 or > 12 ||
                (texto.Length == 2 && texto[1].Length is < 1 or > 6) ||
                texto.Any(parte => !parte.All(char.IsAsciiDigit)) ||
                !decimal.TryParse(item.Quantidade, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantidade))
                throw new ArgumentException("Informe uma quantidade decimal com até seis casas.");
            if (!Enum.TryParse<UnidadeFisicaEstoque>(item.UnidadeBase, out var unidade) || unidade.ToString() != item.UnidadeBase)
                throw new ArgumentException("Informe a unidade-base do material.");

            if (antigos.TryGetValue(item.InsumoId, out var salvo) &&
                salvo.Quantidade == quantidade && salvo.UnidadeBase == unidade) return salvo;
            if (!materiais.TryGetValue(item.InsumoId, out var material) || !material.Ativo)
                throw new ArgumentException("Selecione um material ativo para cada item novo ou alterado.");
            return ItemReceitaProducao.Criar(material, quantidade, unidade);
        }).ToArray();
    }

    private async Task<ReceitaResult> GravarAsync(ReceitaProducao receita, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new(ReceitaStatus.Sucesso, receita);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflito("A receita foi alterada. Atualize o detalhe antes de continuar.");
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflito("Já existe receita ativa para a variante ou a receita foi alterada. Atualize a lista.");
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 547 } sql
            && sql.Message.Contains("FK_ReceitasProducao_ProdutoVariantes_ProdutoVarianteId", StringComparison.Ordinal))
        {
            return Conflito("A variante não está mais disponível. Atualize a lista antes de salvar.");
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 547 } sql
            && sql.Message.Contains("FK_ItensReceitaProducao_InsumosEstoque_InsumoId", StringComparison.Ordinal))
        {
            return Conflito("Um material não está mais disponível. Atualize a lista antes de salvar.");
        }
    }

    private static ReceitaResult Conflito(string erro) => new(ReceitaStatus.Conflito, Erro: erro);
}
