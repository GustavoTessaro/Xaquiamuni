using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class AtendimentoMapping
{
    internal static void Configurar(ModelBuilder builder)
    {
        builder.Entity<ConfiguracaoAtendimento>(e =>
        {
            e.ToTable("ConfiguracoesAtendimento", t =>
            {
                t.HasCheckConstraint("CK_ConfiguracoesAtendimento_Unica", "[Id] = 1");
                t.HasCheckConstraint("CK_ConfiguracoesAtendimento_ValidadePostal",
                    "[ValidadePropostaCorreiosHoras] BETWEEN 1 AND 168");
                t.HasCheckConstraint("CK_ConfiguracoesAtendimento_Modalidade", "[RetiradaHabilitada] = 1 OR [EntregaPropriaHabilitada] = 1");
                t.HasCheckConstraint("CK_ConfiguracoesAtendimento_WhatsApp",
                    "([WhatsAppConfirmado] = 0 OR [WhatsApp] IS NOT NULL) AND ([WhatsAppHabilitado] = 0 OR [WhatsAppConfirmado] = 1)");
            });
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Revisao).IsConcurrencyToken();
            e.Property(c => c.ValidadePropostaCorreiosHoras).HasDefaultValue(48);
            e.Property(c => c.EnderecoRetirada).HasMaxLength(300);
            e.Property(c => c.InstrucoesRetirada).HasMaxLength(600);
            e.Property(c => c.WhatsApp).HasMaxLength(16);
        });

        builder.Entity<CidadeEntregaPropria>(e =>
        {
            e.ToTable("CidadesEntregaPropria", t =>
                t.HasCheckConstraint("CK_CidadesEntregaPropria_Tarifa", "[TarifaCentavos] >= 0"));
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Revisao).IsConcurrencyToken();
            e.Property(c => c.CodigoIbge).HasMaxLength(7).IsFixedLength();
            e.Property(c => c.Nome).HasMaxLength(100);
            e.Property(c => c.Uf).HasMaxLength(2).IsFixedLength();
            e.HasIndex(c => c.CodigoIbge).IsUnique();
        });

        builder.Entity<OperacaoAtendimento>(e =>
        {
            e.ToTable("OperacoesAtendimento");
            e.HasKey(o => new { o.Autor, o.Chave });
            e.Property(o => o.Impressao).HasMaxLength(64).IsFixedLength();
            e.Property(o => o.Operacao).HasMaxLength(24);
        });
    }
}
