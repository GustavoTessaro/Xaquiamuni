using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class VendaExternaMapping
{
    public static void MapearVendasExternas(this ModelBuilder model)
    {
        model.Entity<VendaExterna>(v =>
        {
            v.ToTable("VendasExternas", t => t.HasCheckConstraint("CK_VendasExternas_Total", "[TotalCentavos] > 0"));
            v.Property(x => x.Canal).HasConversion<string>().HasMaxLength(12);
            v.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(12);
            v.Property(x => x.Comprador).HasMaxLength(160);
            v.Property(x => x.DescricaoCanal).HasMaxLength(100);
            v.Property(x => x.MotivoCancelamento).HasMaxLength(500);
            v.Property(x => x.Versao).IsConcurrencyToken();
            v.HasIndex(x => new { x.DataVenda, x.Id });
            v.HasMany(x => x.Itens).WithOne().HasForeignKey("VendaId").IsRequired().OnDelete(DeleteBehavior.Restrict);
            v.HasMany(x => x.Recebimentos).WithOne().HasForeignKey(r => r.VendaId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ItemVendaExterna>(i =>
        {
            i.ToTable("ItensVendaExterna", t => t.HasCheckConstraint("CK_ItensVendaExterna_Valores",
                "[Quantidade] > 0 AND [PrecoUnitarioCentavos] >= 0 AND [DescontoCentavos] >= 0 AND [SubtotalCentavos] >= 0"));
            i.Property(x => x.NomeProduto).HasMaxLength(Produto.NomeMaxLength);
            i.Property(x => x.NomeVariante).HasMaxLength(ProdutoVariante.IdentificacaoMaxLength);
            i.HasIndex("VendaId", nameof(ItemVendaExterna.VarianteId)).IsUnique();
            i.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            i.HasOne<ProdutoVariante>().WithMany().HasForeignKey(x => x.VarianteId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecebimentoVendaExterna>(r =>
        {
            r.ToTable("RecebimentosVendaExterna", t => t.HasCheckConstraint("CK_RecebimentosVendaExterna_Valor", "[ValorCentavos] > 0"));
            r.Property(x => x.Meio).HasConversion<string>().HasMaxLength(12);
            r.HasIndex(x => new { x.ResponsavelId, x.Chave }).IsUnique();
        });
        model.Entity<BaixaVendaExterna>(b =>
        {
            b.ToTable("BaixasVendaExterna", t => t.HasCheckConstraint("CK_BaixasVendaExterna_Quantidade", "[Quantidade] > 0"));
            b.HasKey(x => x.MovimentoId);
            b.HasOne<ItemVendaExterna>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LoteProdutoAcabado>().WithMany().HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MovimentoProdutoAcabado>().WithMany().HasForeignKey(x => x.MovimentoId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MovimentoProdutoAcabado>().WithMany().HasForeignKey(x => x.MovimentoCancelamentoId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.ItemId, x.LoteId }).IsUnique();
            b.HasIndex(x => x.MovimentoCancelamentoId).IsUnique().HasFilter("[MovimentoCancelamentoId] IS NOT NULL");
        });
        model.Entity<OperacaoVendaExterna>(o =>
        {
            o.ToTable("OperacoesVendaExterna");
            o.HasKey(x => new { x.ResponsavelId, x.Chave });
            o.Property(x => x.Tipo).HasMaxLength(12);
            o.Property(x => x.Fingerprint).HasMaxLength(64).IsFixedLength();
            o.Property(x => x.Resultado).HasMaxLength(1024);
            o.HasOne<VendaExterna>().WithMany().HasForeignKey(x => x.VendaId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
