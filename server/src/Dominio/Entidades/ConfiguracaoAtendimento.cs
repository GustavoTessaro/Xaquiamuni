namespace Xaquiamuni.Dominio.Entidades;

public sealed class ConfiguracaoAtendimento
{
    public int Id { get; init; } = 1;
    public bool RetiradaHabilitada { get; set; } = true;
    public bool EntregaPropriaHabilitada { get; set; }
    public bool CotacaoManualCorreiosHabilitada { get; set; }
    public int ValidadePropostaCorreiosHoras { get; set; } = 48;
    public string? EnderecoRetirada { get; set; }
    public string? InstrucoesRetirada { get; set; }
    public string? WhatsApp { get; set; }
    public bool WhatsAppConfirmado { get; set; }
    public bool WhatsAppHabilitado { get; set; }
    public int Revisao { get; set; }
    public Guid AdministradorId { get; set; }
    public DateTimeOffset AlteradoEmUtc { get; set; }
}

public sealed class CidadeEntregaPropria
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string CodigoIbge { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Uf { get; set; } = "";
    public long TarifaCentavos { get; set; }
    public bool Ativa { get; set; }
    public int Revisao { get; set; }
    public Guid CriadoPor { get; init; }
    public DateTimeOffset CriadoEmUtc { get; init; }
    public Guid AlteradoPor { get; set; }
    public DateTimeOffset AlteradoEmUtc { get; set; }
}
