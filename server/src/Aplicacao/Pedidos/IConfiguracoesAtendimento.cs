using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalvarModalidades(bool? RetiradaHabilitada, bool? EntregaPropriaHabilitada,
    bool? CorreiosHabilitados, string? EnderecoRetirada, string? InstrucoesRetirada, int? Revisao);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalvarWhatsApp(string? Numero, bool? Confirmado, bool? Habilitado, int? Revisao);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalvarCidade(string? CodigoIbge, string? Nome, string? Uf,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long? TarifaCentavos,
    bool? Ativa, int? Revisao = null);

public sealed record AtendimentoAdmin(bool RetiradaHabilitada, bool EntregaPropriaHabilitada,
    bool CorreiosHabilitados, string? EnderecoRetirada, string? InstrucoesRetirada,
    string? WhatsApp, bool WhatsAppConfirmado, bool WhatsAppHabilitado, int Revisao,
    Guid? AdministradorId, DateTimeOffset? AlteradoEmUtc);

public sealed record CidadeEntregaDto(Guid Id, string CodigoIbge, string Nome, string Uf,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long TarifaCentavos,
    bool Ativa, int Revisao, Guid CriadoPor, DateTimeOffset CriadoEmUtc, Guid AlteradoPor, DateTimeOffset AlteradoEmUtc)
{
    public static CidadeEntregaDto De(CidadeEntregaPropria cidade) => new(cidade.Id, cidade.CodigoIbge,
        cidade.Nome, cidade.Uf, cidade.TarifaCentavos, cidade.Ativa, cidade.Revisao, cidade.CriadoPor,
        cidade.CriadoEmUtc, cidade.AlteradoPor, cidade.AlteradoEmUtc);
}

public sealed record ModalidadesPublicas(bool RetiradaLocal, bool EntregaPropria, bool Correios)
{
    public string? FreteRetiradaCentavos => RetiradaLocal ? "0" : null;
}

public enum StatusAtendimento { Sucesso, Invalido, Ausente, Conflito }

public sealed record ResultadoAtendimento(StatusAtendimento Status, string? Erro = null, object? Dados = null);

public interface IConfiguracoesAtendimento
{
    Task<AtendimentoAdmin> ConsultarAsync(CancellationToken ct);
    Task<ModalidadesPublicas> ModalidadesAsync(CancellationToken ct);
    Task<string?> ContatoAsync(CancellationToken ct);
    Task<IReadOnlyList<CidadeEntregaDto>> CidadesAsync(CancellationToken ct);
    Task<CidadeEntregaDto?> CidadeAsync(Guid id, CancellationToken ct);
    Task<ResultadoAtendimento> AtualizarModalidadesAsync(SalvarModalidades input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoAtendimento> AtualizarWhatsAppAsync(SalvarWhatsApp input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoAtendimento> SalvarCidadeAsync(Guid? id, SalvarCidade input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoAtendimento> ExcluirCidadeAsync(Guid id, int? revisao, Guid autor, Guid chave, CancellationToken ct);
}
