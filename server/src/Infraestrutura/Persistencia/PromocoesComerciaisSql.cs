using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class PromocoesComerciaisOptions
{
    public bool Habilitadas { get; set; }
}

internal static class PromocoesComerciaisSql
{
    internal static Task<int> BloquearAsync(XaquiamuniDbContext db, bool escrita, CancellationToken ct)
    {
        var modo = escrita ? "Exclusive" : "Shared";
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = 'catalog-promotions', @LockMode = {modo},
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @resultado < 0 THROW 51000, 'Promotion lock unavailable.', 1;
            """, ct);
    }

    internal static async Task<ILookup<Guid, Promocao>> CarregarAsync(XaquiamuniDbContext db,
        Guid[] produtos, DateTimeOffset agora, CancellationToken ct) =>
        (await db.Set<Promocao>().AsNoTracking().Include(p => p.Variantes)
            .Where(p => produtos.Contains(p.ProdutoId) && p.Ativa &&
                (p.InicioUtc == null || p.InicioUtc <= agora) && (p.FimUtc == null || p.FimUtc > agora))
            .ToListAsync(ct)).ToLookup(p => p.ProdutoId);

    internal static PrecoComercial Resolver(IEnumerable<Promocao> promocoes,
        Guid variante, decimal precoRegular, DateTimeOffset agora)
    {
        var regular = checked((long)(precoRegular * 100m));
        var (promocao, preco, _) = PrecosPromocionais.Resolver(promocoes, variante, regular, agora);
        return new(regular, preco?.Final ?? regular, preco?.Economia ?? 0, preco?.OffPontosBase ?? 0, promocao?.Id);
    }
}
