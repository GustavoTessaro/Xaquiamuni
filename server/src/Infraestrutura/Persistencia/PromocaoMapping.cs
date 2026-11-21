using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoPromocao(Guid AdministradorId, Guid Chave, string Impressao, string Resultado);

internal static class PromocaoMapping
{
    internal static void MapearPromocoes(this ModelBuilder model)
    {
        model.Entity<Promocao>(e =>
        {
            e.ToTable("Promocoes", t =>
            {
                t.HasCheckConstraint("CK_Promocoes_Valor", "[Valor] > 0 AND ([Tipo] = 'PrecoFixo' OR ([Tipo] = 'Percentual' AND [Valor] < 10000))");
                t.HasCheckConstraint("CK_Promocoes_Vigencia", "[InicioUtc] IS NULL OR [FimUtc] IS NULL OR [InicioUtc] < [FimUtc]");
                t.HasCheckConstraint("CK_Promocoes_Escopo", "[Escopo] IN ('TodasVariantes', 'VariantesSelecionadas')");
            });
            e.Property(p => p.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Escopo).HasConversion<string>().HasMaxLength(24);
            e.Property(p => p.Versao).IsConcurrencyToken();
            e.HasAlternateKey(p => new { p.Id, p.ProdutoId });
            e.HasIndex(p => new { p.ProdutoId, p.Ativa });
            e.HasOne<Produto>().WithMany().HasForeignKey(p => p.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PromocaoVariante>(e =>
        {
            e.ToTable("PromocoesVariantes");
            e.HasKey(v => new { v.PromocaoId, v.VarianteId });
            e.HasOne<Promocao>().WithMany(p => p.Variantes)
                .HasForeignKey(v => new { v.PromocaoId, v.ProdutoId }).HasPrincipalKey(p => new { p.Id, p.ProdutoId });
            e.HasOne<ProdutoVariante>().WithMany()
                .HasForeignKey(v => new { v.ProdutoId, v.VarianteId }).HasPrincipalKey(v => new { v.ProdutoId, v.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OperacaoPromocao>(e =>
        {
            e.ToTable("OperacoesPromocao");
            e.HasKey(o => new { o.AdministradorId, o.Chave });
            e.Property(o => o.Impressao).HasMaxLength(64).IsFixedLength();
        });
    }
}
