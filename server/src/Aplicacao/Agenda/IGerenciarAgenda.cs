using System.Text.Json.Serialization;

namespace Xaquiamuni.Aplicacao.Agenda;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompromissoAgendaInput(string? Titulo, string? Descricao, string? Categoria,
    DateOnly Data, TimeOnly? HorarioInicial, TimeOnly? HorarioFinal, Guid? Versao);

public sealed record CompromissoAgendaDto(Guid Id, Guid Versao, string Titulo, string? Descricao, string? Categoria,
    DateOnly Data, TimeOnly? HorarioInicial, TimeOnly? HorarioFinal, Guid CriadoPorId, DateTimeOffset CriadoEmUtc,
    Guid AtualizadoPorId, DateTimeOffset AtualizadoEmUtc, Guid? ExcluidoPorId, DateTimeOffset? ExcluidoEmUtc);

public sealed record TentativaAgendaDto(string Operacao, Guid RecursoId, CompromissoAgendaDto Resultado);
public enum StatusAgenda { Sucesso, Repetida, Invalida, Ausente, Conflito }
public sealed record ResultadoAgenda(StatusAgenda Status, CompromissoAgendaDto? Dados = null, string? Erro = null);

public interface IGerenciarAgenda
{
    Task<IReadOnlyList<CompromissoAgendaDto>> ListarAsync(DateOnly? inicio, DateOnly? fim, CancellationToken ct);
    Task<CompromissoAgendaDto?> ConsultarAsync(Guid id, CancellationToken ct);
    Task<TentativaAgendaDto?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoAgenda> CriarAsync(CompromissoAgendaInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoAgenda> EditarAsync(Guid id, CompromissoAgendaInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoAgenda> ExcluirAsync(Guid id, Guid versao, Guid autor, Guid chave, CancellationToken ct);
}
