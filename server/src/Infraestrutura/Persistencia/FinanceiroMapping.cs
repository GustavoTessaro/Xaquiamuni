using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoFinanceira(Guid ResponsavelId, Guid Chave, string Impressao, string Resultado);

internal static class FinanceiroMapping
{
    internal static void MapearFinanceiro(this ModelBuilder model)
    {
        model.Entity<LancamentoFinanceiro>(e =>
        {
            e.ToTable("LancamentosFinanceiros", t => t.HasCheckConstraint("CK_LancamentosFinanceiros_Valor", "[ValorCentavos] > 0"));
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Meio).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Categoria).HasMaxLength(80);
            e.Property(x => x.Descricao).HasMaxLength(300);
            e.Property(x => x.MotivoCancelamento).HasMaxLength(500);
            e.HasIndex(x => new { x.Data, x.Id });
        });
        model.Entity<PagamentoFornecedor>(e =>
        {
            e.ToTable("PagamentosFornecedores", t => t.HasCheckConstraint("CK_PagamentosFornecedores_Valor", "[ValorCentavos] > 0"));
            e.Property(x => x.Meio).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Referencia).HasMaxLength(300);
            e.HasOne<CompraMateriaPrima>().WithMany().HasForeignKey(x => x.CompraId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CompraId, x.Data, x.Id });
        });
        model.Entity<OperacaoFinanceira>(e =>
        {
            e.ToTable("OperacoesFinanceiras");
            e.HasKey(x => new { x.ResponsavelId, x.Chave });
            e.Property(x => x.Impressao).HasMaxLength(64).IsFixedLength();
        });
    }
}

internal static class LockFinanceiroSql
{
    internal static Task<int> CompraAsync(XaquiamuniDbContext db, Guid id, CancellationToken ct) =>
        AdquirirAsync(db, $"finance-purchase:{id:D}", ct);

    internal static Task<int> AdquirirAsync(XaquiamuniDbContext db, string recurso, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @resultado < 0 THROW 51000, 'Finance lock unavailable.', 1;
            """, ct);
}
