using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class CompraMateriaPrimaMapping
{
    internal static void MapearComprasMateriasPrimas(this ModelBuilder model)
    {
        model.Entity<CompraMateriaPrima>(compra =>
        {
            compra.ToTable("ComprasMateriasPrimas", t => t.UseSqlOutputClause(false));
            compra.Property(c => c.NomeFornecedor).HasMaxLength(Fornecedor.NomeMax);
            compra.Property(c => c.Situacao).HasConversion<string>().HasMaxLength(20);
            compra.Property(c => c.Total).HasPrecision(18, 2);
            compra.Property(c => c.Versao).IsConcurrencyToken();
            compra.Property(c => c.ImpressaoCriacao).HasMaxLength(64).IsFixedLength();
            compra.HasIndex(c => new { c.CriadoPorId, c.ChaveCriacao }).IsUnique()
                .HasDatabaseName("UX_ComprasMateriasPrimas_Criacao");
            compra.Property(c => c.ImpressaoRecebimento).HasMaxLength(64).IsFixedLength();
            compra.HasIndex(c => new { c.RecebidaPorId, c.ChaveRecebimento }).IsUnique()
                .HasFilter("[RecebidaPorId] IS NOT NULL AND [ChaveRecebimento] IS NOT NULL");
            compra.HasIndex(c => new { c.DataCompra, c.Id });
            compra.HasOne<Fornecedor>().WithMany().HasForeignKey(c => c.FornecedorId).OnDelete(DeleteBehavior.Restrict);
            compra.HasMany(c => c.Linhas).WithOne().HasForeignKey("CompraId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<EntradaRecebimentoCompra>(entrada =>
        {
            entrada.ToTable("EntradasRecebimentoCompra");
            entrada.HasKey(e => e.ComponenteId);
            entrada.HasIndex(e => e.MovimentoId).IsUnique();
            entrada.HasIndex(e => new { e.CompraId, e.Ordem }).IsUnique();
            entrada.HasOne<CompraMateriaPrima>().WithMany().HasForeignKey(e => e.CompraId).OnDelete(DeleteBehavior.Restrict);
            entrada.HasOne<LinhaCompraMateriaPrima>().WithMany().HasForeignKey(e => e.LinhaId).OnDelete(DeleteBehavior.Restrict);
            entrada.HasOne<ComponenteCompraMateriaPrima>().WithMany().HasForeignKey(e => e.ComponenteId).OnDelete(DeleteBehavior.Restrict);
            entrada.HasOne<MovimentoInsumo>().WithMany().HasForeignKey(e => e.MovimentoId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LinhaCompraMateriaPrima>(linha =>
        {
            linha.ToTable("LinhasCompraMateriaPrima");
            linha.Property(l => l.Kit).HasMaxLength(120);
            linha.Property(l => l.Quantidade).HasPrecision(18, 6);
            linha.Property(l => l.ValorTotal).HasPrecision(18, 2);
            linha.HasMany(l => l.Componentes).WithOne().HasForeignKey("LinhaId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ComponenteCompraMateriaPrima>(componente =>
        {
            componente.ToTable("ComponentesCompraMateriaPrima", t => t.UseSqlOutputClause(false));
            componente.Property(c => c.NomeInsumo).HasMaxLength(InsumoEstoque.NomeMaxLength);
            componente.Property(c => c.NomeApresentacao).HasMaxLength(FormaCompraInsumo.NomeMaxLength);
            componente.Property(c => c.Quantidade).HasPrecision(18, 6);
            componente.Property(c => c.Fator).HasPrecision(18, 6);
            componente.Property(c => c.QuantidadeBase).HasPrecision(18, 6);
            componente.Property(c => c.UnidadeBase).HasConversion<string>().HasMaxLength(20);
            componente.HasOne<InsumoEstoque>().WithMany().HasForeignKey(c => c.InsumoId).OnDelete(DeleteBehavior.Restrict);
            componente.HasOne<FormaCompraInsumo>().WithMany().HasForeignKey(c => c.ApresentacaoId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
