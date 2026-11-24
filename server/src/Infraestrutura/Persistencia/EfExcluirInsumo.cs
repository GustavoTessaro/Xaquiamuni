using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoExclusaoInsumo(Guid Autor, Guid Chave, Guid InsumoId, Guid? FormaCompraId,
    string Nome, string FormasJson, string Versao, DateTimeOffset InstanteUtc, string Resultado);

internal sealed record EvidenciaUsoInsumo(Guid InsumoId, Guid FormaCompraId, string Origem, bool Comprovada)
{
    // Guid.Empty identifica evidência do insumo, sem uma apresentação específica.
    internal static Task RegistrarContagemAsync(XaquiamuniDbContext db, Guid insumo, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            MERGE [EvidenciasUsoInsumos] WITH (HOLDLOCK) AS alvo
            USING (VALUES ({insumo}, {Guid.Empty}, N'Contagem')) AS origem (InsumoId, FormaCompraId, Origem)
            ON alvo.InsumoId = origem.InsumoId AND alvo.FormaCompraId = origem.FormaCompraId AND alvo.Origem = origem.Origem
            WHEN MATCHED AND alvo.Comprovada = 0 THEN UPDATE SET Comprovada = 1
            WHEN NOT MATCHED THEN INSERT (InsumoId, FormaCompraId, Origem, Comprovada)
                VALUES (origem.InsumoId, origem.FormaCompraId, origem.Origem, 1);
            """, ct);
}

public sealed class EfExcluirInsumo(XaquiamuniDbContext db, TimeProvider relogio) : IExcluirInsumo
{
    public async Task<(int Status, string? Codigo)> ExecutarAsync(Guid insumo, Guid? forma, string versao,
        Guid autor, Guid chave, CancellationToken ct)
    {
        try
        {
            await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await DependenciasExclusaoCatalogo.BloquearAsync(db, $"supply-delete-key:{autor:D}:{chave:D}", ct);
            var auditoria = db.Set<OperacaoExclusaoInsumo>();
            var anterior = await auditoria.FromSqlInterpolated($"""
                SELECT * FROM OperacoesExclusaoInsumo WITH (READCOMMITTEDLOCK)
                WHERE Autor = {autor} AND Chave = {chave}
                """).AsNoTracking().SingleOrDefaultAsync(ct);
            if (anterior is not null)
                return anterior.InsumoId == insumo && anterior.FormaCompraId == forma && anterior.Versao == versao
                    ? (204, null) : (409, "chave_em_uso");

            await BloquearVinculoAsync(db, insumo, ct);
            var cadastro = await db.InsumosEstoque.Include(i => i.FormasCompra).SingleOrDefaultAsync(i => i.Id == insumo, ct);
            if (cadastro is null) return (404, "insumo_nao_encontrado");
            var apresentacao = forma.HasValue ? cadastro.FormasCompra.SingleOrDefault(f => f.Id == forma) : null;
            if (forma.HasValue && apresentacao is null) return (404, "forma_compra_nao_encontrada");
            if (VersaoCadastroInsumo.Calcular(cadastro) != versao) return (409, "versao_desatualizada");
            var impedimento = await VerificarAsync(insumo, forma, ct);
            if (impedimento is not null) return (409, impedimento);

            IEnumerable<FormaCompraInsumo> removidas = apresentacao is null ? cadastro.FormasCompra : [apresentacao];
            var formas = removidas.Select(f => f.Id).Order().ToArray();
            db.RemoveRange(removidas.ToArray());
            if (apresentacao is null) db.Remove(cadastro);
            auditoria.Add(new(autor, chave, insumo, forma, apresentacao?.Nome ?? cadastro.Nome,
                JsonSerializer.Serialize(formas), versao, relogio.GetUtcNow(), "Excluido"));
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return (204, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (409, "versao_desatualizada");
        }
        catch (Exception e) when (EfFornecedorRepository.ErroLock(e))
        {
            return (409, "conflito_concorrencia");
        }
    }

    internal static Task BloquearVinculoAsync(XaquiamuniDbContext db, Guid insumo, CancellationToken ct) =>
        DependenciasExclusaoCatalogo.BloquearAsync(db, $"stock-supply:{insumo:D}", ct);

    private async Task<string?> VerificarAsync(Guid insumo, Guid? forma, CancellationToken ct)
    {
        var usos = db.Set<EvidenciaUsoInsumo>().Where(u => u.InsumoId == insumo);
        var comprovados = usos.Where(u => u.Comprovada);
        if (forma is Guid id)
        {
            if (await db.Set<ComponenteCompraMateriaPrima>().AnyAsync(c => c.ApresentacaoId == id, ct)
                || await db.MovimentosInsumo.AnyAsync(m => m.FormaCompraId == id, ct)
                || await comprovados.AnyAsync(u => u.FormaCompraId == id, ct))
                return "forma_compra_com_historico";
            return await usos.AnyAsync(u => !u.Comprovada && u.FormaCompraId == id, ct)
                || await db.MovimentosInsumo.AnyAsync(m => m.InsumoId == insumo &&
                    (m.FormaCompraId == Guid.Empty || (m.FormaCompraId == null && m.FormaCompraNome != null)), ct)
                || await db.Set<ComponenteCompraMateriaPrima>().AnyAsync(c => c.InsumoId == insumo &&
                    c.ApresentacaoId == null && c.NomeApresentacao != null, ct)
                ? "forma_compra_historico_inconclusivo" : null;
        }

        var formas = db.FormasCompraInsumo.Where(f => f.InsumoId == insumo).Select(f => f.Id);
        if (await db.Set<ComponenteCompraMateriaPrima>().AnyAsync(c => c.InsumoId == insumo
                || (c.ApresentacaoId.HasValue && formas.Contains(c.ApresentacaoId.Value)), ct)
            || await comprovados.AnyAsync(u => u.Origem == "Compra", ct))
            return "insumo_com_compras";
        if (await db.LotesInsumo.AnyAsync(l => l.InsumoId == insumo, ct)
            || await db.SaldosInsumo.AnyAsync(s => s.InsumoId == insumo, ct)
            || await db.MovimentosInsumo.AnyAsync(m => m.InsumoId == insumo
                || (m.FormaCompraId.HasValue && formas.Contains(m.FormaCompraId.Value)), ct)
            || await comprovados.AnyAsync(u => u.Origem == "Contagem", ct))
            return "insumo_com_historico_estoque";
        if (await db.Set<NecessidadeOrdemProducao>().AnyAsync(n => n.InsumoId == insumo, ct))
            return "insumo_com_producao";
        if (await db.Set<ItemReceitaProducao>().AnyAsync(i => i.InsumoId == insumo, ct))
            return "insumo_com_receitas";
        return await usos.AnyAsync(u => !u.Comprovada, ct) ? "insumo_historico_inconclusivo" : null;
    }
}
