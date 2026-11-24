using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoExclusaoReceita(Guid Autor, Guid Chave, Guid ReceitaId, Guid? RevisaoId,
    string Nome, string RevisoesJson, Guid Token, DateTimeOffset InstanteUtc, string Resultado);

public sealed class EfExcluirReceita(XaquiamuniDbContext db, TimeProvider relogio) : IExcluirReceita
{
    internal static Task BloquearAsync(XaquiamuniDbContext db, Guid receita, CancellationToken ct) =>
        DependenciasExclusaoCatalogo.BloquearAsync(db, $"production-recipe:{receita:D}", ct);

    public async Task<(int Status, string? Codigo)> ExecutarAsync(Guid receita, Guid? revisao, Guid token,
        Guid autor, Guid chave, CancellationToken ct)
    {
        try
        {
            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            await DependenciasExclusaoCatalogo.BloquearAsync(db, $"recipe-delete-key:{autor:D}:{chave:D}", ct);
            var auditoria = db.Set<OperacaoExclusaoReceita>();
            var anterior = await auditoria.AsNoTracking().SingleOrDefaultAsync(a => a.Autor == autor && a.Chave == chave, ct);
            if (anterior is not null)
                return anterior.ReceitaId == receita && anterior.RevisaoId == revisao && anterior.Token == token
                    ? (204, null) : (409, "chave_em_uso");

            await BloquearAsync(db, receita, ct);
            var cadastro = await db.ReceitasProducao.Include(r => r.Versoes).ThenInclude(v => v.Itens)
                .SingleOrDefaultAsync(r => r.Id == receita, ct);
            if (cadastro is null) return (404, "receita_nao_encontrada");
            var selecionada = cadastro.Versoes.SingleOrDefault(v => v.Id == revisao);
            if (revisao.HasValue && selecionada is null) return (404, "versao_receita_nao_encontrada");
            if (cadastro.Token != token) return (409, "versao_desatualizada");
            if (cadastro.Versoes.Count == 0) return (409, "receita_historico_inconclusivo");

            var removidas = selecionada is null ? cadastro.Versoes.ToArray() : [selecionada];
            var ids = removidas.Select(v => v.Id).ToArray();
            if (await db.OrdensProducao.AnyAsync(o => ids.Contains(o.ReceitaVersaoId), ct))
                return (409, selecionada is null ? "receita_com_producao" : "versao_receita_com_producao");
            if (selecionada is not null && cadastro.Versoes.Count == 1)
                return (409, "ultima_versao_receita");
            if (selecionada == cadastro.Atual) return (409, "versao_receita_atual");

            var agora = relogio.GetUtcNow();
            var resumo = removidas.OrderBy(v => v.Numero).Select(v => new { v.Id, v.Numero });
            auditoria.Add(new(autor, chave, receita, revisao, cadastro.Nome,
                JsonSerializer.Serialize(resumo), token, agora, "Excluido"));
            if (selecionada is not null)
                cadastro.RemoverVersaoAnterior(selecionada, autor, agora);
            db.RemoveRange(removidas.SelectMany(v => v.Itens));
            db.RemoveRange(removidas);
            if (selecionada is null)
                db.Remove(cadastro);
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return (204, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (409, "versao_desatualizada");
        }
        catch (Exception erro) when (EfFornecedorRepository.ErroLock(erro))
        {
            return (409, "conflito_concorrencia");
        }
    }
}
