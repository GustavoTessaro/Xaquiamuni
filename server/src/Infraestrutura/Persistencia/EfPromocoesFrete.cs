using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfPromocoesFrete(XaquiamuniDbContext db, TimeProvider relogio)
{
    public const int TamanhoPagina = 30;

    private static Task<int> BloquearAsync(XaquiamuniDbContext db, bool escrita, CancellationToken ct)
    {
        var modo = escrita ? "Exclusive" : "Shared";
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = 'shipping-promotions', @LockMode = {modo},
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @resultado < 0 THROW 51000, 'Shipping promotion lock unavailable.', 1;
            """, ct);
    }

    // O lock compartilhado impede alterações e ativações entre a leitura e o commit comercial.
    internal static async Task<(long Frete, DadosDescontoFrete? Desconto, int? Revisao)> CalcularEntregaPropriaAsync(
        XaquiamuniDbContext db, long original, long subtotal, DateTimeOffset agora, CancellationToken ct)
    {
        if (original == 0) return (0, null, null);
        await BloquearAsync(db, escrita: false, ct);
        var candidatas = await db.Set<PromocaoFrete>().AsNoTracking()
            .Where(c => c.Ativa && (c.Modalidades & ModalidadesPromocaoFrete.EntregaPropria) != 0 &&
                c.SubtotalMinimoCentavos <= subtotal &&
                (c.InicioUtc == null || c.InicioUtc <= agora) && (c.FimUtc == null || c.FimUtc > agora))
            .ToListAsync(ct);
        var resultado = DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, original, subtotal, agora, candidatas);
        if (resultado.CampanhaId is not { } id) return (original, null, null);
        var campanha = candidatas.Single(c => c.Id == id);
        return (resultado.LiquidoCentavos,
            new DadosDescontoFrete(id, campanha.Nome, original, resultado.DescontoCentavos), campanha.Versao);
    }

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
        try
        {
            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            await BloquearAsync(db, escrita: true, ct);
            var campanha = await db.Set<PromocaoFrete>().FindAsync([id], ct);
            if (campanha is null) return (null, false);
            if (campanha.Versao != versao) return (null, true);

            var agora = relogio.GetUtcNow();
            if (dados is not null) campanha.Editar(dados, autor, agora);
            else campanha.AlterarStatus(ativa!.Value, autor, agora);

            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return (campanha, false);
        }
        catch (Exception e) when (e is DbUpdateConcurrencyException or SqlException { Number: 1205 or 1222 or 51000 })
        {
            return (null, true);
        }
    }
}
