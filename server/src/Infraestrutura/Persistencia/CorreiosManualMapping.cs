using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed class SolicitacaoCorreios
{
    public Guid Id { get; init; }
    public Guid ClienteId { get; init; }
    public DateTimeOffset CriadaEmUtc { get; init; }
    public string ItensJson { get; init; } = "";
    public string EnderecoJson { get; init; } = "";
    public string Impressao { get; init; } = "";
    public int Versao { get; set; } = 1;
    public SituacaoCotacaoCorreios Situacao { get; set; } = SituacaoCotacaoCorreios.AguardandoAnalise;
    public int? Revisao { get; set; }
    public int? RevisaoAceita { get; set; }
    public DateTimeOffset? ValidaAteUtc { get; set; }

    public SituacaoCotacaoCorreios SituacaoEm(DateTimeOffset agora) =>
        Situacao is (SituacaoCotacaoCorreios.PropostaDisponivel or SituacaoCotacaoCorreios.Aceita) && ValidaAteUtc <= agora
            ? SituacaoCotacaoCorreios.Expirada : Situacao;
}

internal sealed record PropostaCorreios(Guid SolicitacaoId, int Revisao, string Servico, long FreteCentavos,
    int PrazoDias, DateTimeOffset ValidaAteUtc, int? PesoTotalGramas, string VolumesJson,
    DateTimeOffset PublicadaEmUtc, Guid PublicadaPor);

internal sealed record OperacaoCotacaoCorreios(Guid Autor, bool Admin, Guid Chave, Guid SolicitacaoId, string Impressao,
    AcaoCotacaoCorreios Acao, DateTimeOffset ExecutadaEmUtc, string? Motivo, string ResultadoJson);

public sealed class CorreiosManualOptions
{
    public Dictionary<string, MunicipioPostal> MunicipiosPorCep { get; init; } = [];
}
public sealed record MunicipioPostal(string CodigoIbge, string Cidade, string Uf)
{
    public MunicipioPostal() : this("", "", "") { }
}

internal static class CorreiosManualMapping
{
    internal static void Configurar(ModelBuilder builder)
    {
        builder.Entity<SolicitacaoCorreios>(e =>
        {
            e.ToTable("SolicitacoesCorreios", t =>
            {
                t.HasCheckConstraint("CK_SolicitacoesCorreios_Situacao", "[Situacao] BETWEEN 1 AND 7 AND [Versao] > 0");
                t.HasCheckConstraint("CK_SolicitacoesCorreios_Revisao",
                    "([Revisao] IS NULL AND [ValidaAteUtc] IS NULL OR [Revisao] > 0 AND [ValidaAteUtc] IS NOT NULL) "
                    + "AND ([RevisaoAceita] IS NULL OR [RevisaoAceita] = [Revisao])");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Versao).IsConcurrencyToken();
            e.Property(x => x.Impressao).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => new { x.ClienteId, x.CriadaEmUtc });
            e.HasIndex(x => new { x.Situacao, x.CriadaEmUtc });
        });
        builder.Entity<PropostaCorreios>(e =>
        {
            e.ToTable("PropostasCorreios", t => t.HasCheckConstraint("CK_PropostasCorreios_Valores",
                "[Revisao] > 0 AND [FreteCentavos] >= 0 AND [PrazoDias] > 0 AND [ValidaAteUtc] > [PublicadaEmUtc]"));
            e.HasKey(x => new { x.SolicitacaoId, x.Revisao });
            e.Property(x => x.Servico).HasMaxLength(60);
            e.HasOne<SolicitacaoCorreios>().WithMany().HasForeignKey(x => x.SolicitacaoId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OperacaoCotacaoCorreios>(e =>
        {
            e.ToTable("OperacoesCotacoesCorreios");
            e.HasKey(x => new { x.Autor, x.Admin, x.Chave });
            e.Property(x => x.Impressao).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Motivo).HasMaxLength(300);
            e.HasOne<SolicitacaoCorreios>().WithMany().HasForeignKey(x => x.SolicitacaoId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
