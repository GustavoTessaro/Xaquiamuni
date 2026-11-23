using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfPromocoesFrete(XaquiamuniDbContext db, TimeProvider relogio)
{
    public const int TamanhoPagina = 30;

    public Task<List<PromocaoFrete>> ListarAsync(int pagina, CancellationToken ct) =>
        db.Set<PromocaoFrete>().AsNoTracking().OrderByDescending(c => c.CriadaEmUtc).ThenBy(c => c.Id)
            .Skip((pagina - 1) * TamanhoPagina).Take(TamanhoPagina + 1).ToListAsync(ct);

    public Task<PromocaoFrete?> ConsultarAsync(Guid id, CancellationToken ct) =>
        db.Set<PromocaoFrete>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<PromocaoFrete> CriarAsync(ConfiguracaoPromocaoFrete dados, Guid autor, CancellationToken ct)
    {
        var campanha = PromocaoFrete.Criar(dados, autor, relogio.GetUtcNow());
        db.Add(campanha);
        await db.SaveChangesAsync(ct);
        return campanha;
    }

    public async Task<(PromocaoFrete? Campanha, bool Conflito)> AlterarAsync(Guid id, int versao,
        ConfiguracaoPromocaoFrete? dados, bool? ativa, Guid autor, CancellationToken ct)
    {
        var campanha = await db.Set<PromocaoFrete>().FindAsync([id], ct);
        if (campanha is null) return (null, false);
        if (campanha.Versao != versao) return (null, true);

        var agora = relogio.GetUtcNow();
        if (dados is not null) campanha.Editar(dados, autor, agora);
        else campanha.AlterarStatus(ativa!.Value, autor, agora);

        try
        {
            await db.SaveChangesAsync(ct);
            return (campanha, false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, true);
        }
    }
}
