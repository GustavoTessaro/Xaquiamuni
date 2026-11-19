using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class ReceitaProducaoMapping
{
    public static void MapearReceitasProducao(this ModelBuilder model)
    {
        model.Entity<ReceitaProducao>(r =>
        {
            r.ToTable("ReceitasProducao");
            r.Ignore(x => x.Atual);
            r.Property(x => x.Nome).HasMaxLength(ReceitaProducao.NomeMax);
            r.Property(x => x.Token).IsConcurrencyToken();
            r.HasIndex(x => x.ProdutoVarianteId).IsUnique().HasFilter("[Ativa] = 1")
                .HasDatabaseName("UX_ReceitasProducao_VarianteAtiva");
            r.HasOne(x => x.Variante).WithMany().HasForeignKey(x => x.ProdutoVarianteId).OnDelete(DeleteBehavior.Restrict);
            r.HasMany(x => x.Versoes).WithOne().HasForeignKey("ReceitaId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ReceitaProducaoVersao>(v =>
        {
            v.ToTable("ReceitasProducaoVersoes", t =>
                t.HasCheckConstraint("CK_ReceitasProducaoVersoes_Positiva", "[Numero] > 0 AND [Rendimento] > 0"));
            v.HasIndex("ReceitaId", nameof(ReceitaProducaoVersao.Numero)).IsUnique();
            v.HasMany(x => x.Itens).WithOne().HasForeignKey("VersaoId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ItemReceitaProducao>(i =>
        {
            i.ToTable("ItensReceitaProducao", t =>
                t.HasCheckConstraint("CK_ItensReceitaProducao_Quantidade", "[Quantidade] > 0 AND ([UnidadeBase] IN ('Grama','Mililitro') OR ([UnidadeBase] = 'Unidade' AND [Quantidade] = FLOOR([Quantidade])))"));
            i.Property(x => x.NomeInsumo).HasMaxLength(InsumoEstoque.NomeMaxLength);
            i.Property(x => x.UnidadeBase).HasConversion<string>().HasMaxLength(20);
            i.Property(x => x.Quantidade).HasPrecision(18, 6);
            i.HasIndex("VersaoId", nameof(ItemReceitaProducao.InsumoId)).IsUnique();
            i.HasOne<InsumoEstoque>().WithMany().HasForeignKey(x => x.InsumoId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
