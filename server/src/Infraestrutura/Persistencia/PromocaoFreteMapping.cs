using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class PromocaoFreteMapping
{
    internal static void MapearPromocoesFrete(this ModelBuilder model) => model.Entity<PromocaoFrete>(e =>
    {
        e.ToTable("PromocoesFrete", t =>
        {
            t.HasCheckConstraint("CK_PromocoesFrete_Valores", "[SubtotalMinimoCentavos] >= 0 AND "
                + "(([Tipo] = 1 AND [Valor] BETWEEN 1 AND 10000) OR ([Tipo] = 2 AND [Valor] > 0) OR ([Tipo] = 3 AND [Valor] = 0))");
            t.HasCheckConstraint("CK_PromocoesFrete_Vigencia", "([InicioUtc] IS NULL OR DATEPART(TZOFFSET, [InicioUtc]) = 0) AND "
                + "([FimUtc] IS NULL OR DATEPART(TZOFFSET, [FimUtc]) = 0) AND ([InicioUtc] IS NULL OR [FimUtc] IS NULL OR [InicioUtc] < [FimUtc])");
            t.HasCheckConstraint("CK_PromocoesFrete_Modalidades", "[Modalidades] IN (1, 2, 3)");
            t.HasCheckConstraint("CK_PromocoesFrete_NomeVersao", "LEN(LTRIM(RTRIM([Nome]))) > 0 AND [Versao] > 0");
        });
        e.Property(c => c.Nome).HasMaxLength(PromocaoFrete.LimiteNome);
        e.Property(c => c.Versao).IsConcurrencyToken();
        e.HasIndex(c => new { c.Ativa, c.FimUtc });
        e.HasIndex(c => new { c.CriadaEmUtc, c.Id }).IsDescending(true, false);
    });
}
