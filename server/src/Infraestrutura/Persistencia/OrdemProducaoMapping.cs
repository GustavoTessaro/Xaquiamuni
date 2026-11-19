using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class OrdemProducaoMapping
{
    internal static void MapearOrdensProducao(this ModelBuilder model)
    {
        model.Entity<OrdemProducao>(ordem =>
        {
            ordem.ToTable("OrdensProducao", t =>
                t.HasCheckConstraint("CK_OrdensProducao_Valida",
                    "[QuantidadePlanejada] > 0 AND (([Situacao] = 'EmAndamento' AND [EncerradaEmUtc] IS NULL AND [EncerradaPorId] IS NULL) OR ([Situacao] IN ('Concluida', 'Cancelada') AND [EncerradaEmUtc] IS NOT NULL AND [EncerradaPorId] IS NOT NULL))"));
            ordem.Property(o => o.Situacao).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            ordem.HasOne<ProdutoVariante>().WithMany().HasForeignKey(o => o.ProdutoVarianteId).OnDelete(DeleteBehavior.Restrict);
            ordem.HasOne<ReceitaProducaoVersao>().WithMany().HasForeignKey(o => o.ReceitaVersaoId).OnDelete(DeleteBehavior.Restrict);
            ordem.HasMany(o => o.Necessidades).WithOne(n => n.Ordem).HasForeignKey(n => n.OrdemId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<NecessidadeOrdemProducao>(necessidade =>
        {
            necessidade.ToTable("NecessidadesOrdemProducao", t => t.HasCheckConstraint(
                "CK_NecessidadesOrdemProducao_Quantidade",
                "[Quantidade] > 0 AND ([UnidadeBase] IN ('Grama','Mililitro') OR ([UnidadeBase] = 'Unidade' AND [Quantidade] = FLOOR([Quantidade])))"));
            necessidade.Property(n => n.NomeInsumo).HasMaxLength(InsumoEstoque.NomeMaxLength);
            necessidade.Property(n => n.Quantidade).HasPrecision(18, 6);
            necessidade.Property(n => n.UnidadeBase).HasConversion<string>().HasMaxLength(20);
            necessidade.HasIndex(n => new { n.OrdemId, n.InsumoId }).IsUnique();
            necessidade.HasIndex(n => n.InsumoId);
            necessidade.HasOne<InsumoEstoque>().WithMany().HasForeignKey(n => n.InsumoId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AlocacaoMateriaPrima>(alocacao =>
        {
            alocacao.ToTable("AlocacoesMateriasPrimas", t => t.HasCheckConstraint("CK_AlocacoesMateriasPrimas_Quantidade",
                "[Reservada] > 0 AND [Consumida] >= 0 AND [Consumida] <= [Reservada]"));
            alocacao.HasKey(a => new { a.NecessidadeId, a.SaldoId });
            alocacao.Property(a => a.Reservada).HasPrecision(18, 6);
            alocacao.Property(a => a.Consumida).HasPrecision(18, 6);
            alocacao.HasOne(a => a.Necessidade).WithMany().HasForeignKey(a => a.NecessidadeId).OnDelete(DeleteBehavior.Restrict);
            alocacao.HasOne(a => a.Saldo).WithMany().HasForeignKey(a => a.SaldoId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
