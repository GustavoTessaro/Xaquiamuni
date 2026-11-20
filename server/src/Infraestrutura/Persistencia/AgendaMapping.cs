using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Agenda;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoAgenda(Guid ResponsavelId, Guid Chave, string Operacao,
    string Impressao, string Resultado);

internal static class AgendaMapping
{
    internal const string ChaveOperacao = "PK_OperacoesAgenda";

    internal static void MapearAgenda(this ModelBuilder model)
    {
        model.Entity<CompromissoAgenda>(e =>
        {
            e.ToTable("CompromissosAgenda", t => t.HasCheckConstraint("CK_CompromissosAgenda_Horarios",
                "[HorarioFinal] IS NULL OR ([HorarioInicial] IS NOT NULL AND [HorarioFinal] > [HorarioInicial])"));
            e.Property(x => x.Versao).IsConcurrencyToken();
            e.Property(x => x.Titulo).HasMaxLength(CompromissoAgenda.TituloMaximo);
            e.Property(x => x.Descricao).HasMaxLength(CompromissoAgenda.DescricaoMaxima);
            e.Property(x => x.Categoria).HasMaxLength(CompromissoAgenda.CategoriaMaxima);
            e.HasIndex(x => new { x.Data, x.HorarioInicial, x.Id }).HasFilter("[ExcluidoEmUtc] IS NULL");
        });

        model.Entity<OperacaoAgenda>(e =>
        {
            e.ToTable("OperacoesAgenda");
            e.HasKey(x => new { x.ResponsavelId, x.Chave }).HasName(ChaveOperacao);
            e.Property(x => x.Operacao).HasMaxLength(10);
            e.Property(x => x.Impressao).HasMaxLength(64).IsFixedLength();
        });
    }
}
