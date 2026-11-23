using System.Text.Json.Serialization;

namespace Xaquiamuni.Aplicacao.Pedidos;

public enum SituacaoCotacaoCorreios : short
{
    AguardandoAnalise = 1, PropostaDisponivel, Aceita, Recusada, Expirada, Cancelada, ConvertidaEmPedido,
}

public enum AcaoCotacaoCorreios : short { Criar = 1, Publicar, Aceitar, Recusar, Cancelar }

public sealed record ItemSolicitadoCorreios(Guid ProdutoId, Guid ProdutoVarianteId, int Quantidade);
public sealed record ItemCongeladoCorreios(Guid ProdutoId, Guid ProdutoVarianteId, int Quantidade,
    string NomeProduto, string NomeVariante);

public sealed record SolicitarCotacaoCorreios(ItemSolicitadoCorreios[]? Itens, EnderecoCotacaoEntrega? Endereco);

public sealed record VolumeCorreios(int PesoGramas, decimal? ComprimentoCm, decimal? LarguraCm, decimal? AlturaCm);

public sealed record PublicarPropostaCorreios(string? Servico,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long? FreteCentavos,
    int? PrazoDias, DateTimeOffset? ValidaAteUtc, int? PesoTotalGramas, VolumeCorreios[]? Volumes);

public sealed record DecidirCotacaoCorreios(int? Revisao = null, string? Motivo = null);
public sealed record PropostaCorreiosDto(int Revisao, string Servico,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long FreteCentavos,
    int PrazoDias, DateTimeOffset ValidaAteUtc, int? PesoTotalGramas, IReadOnlyList<VolumeCorreios> Volumes,
    DateTimeOffset PublicadaEmUtc);

public sealed record ResumoCotacaoCorreios(Guid Id, string Situacao, DateTimeOffset CriadaEmUtc,
    int? Revisao, DateTimeOffset? ValidaAteUtc);
public sealed record DetalheCotacaoCorreios(ResumoCotacaoCorreios Resumo, int Versao,
    IReadOnlyList<ItemCongeladoCorreios> Itens, Xaquiamuni.Dominio.Entidades.EnderecoEntregaPedido Endereco,
    IReadOnlyList<PropostaCorreiosDto> Propostas, int? RevisaoAceita, Guid? PedidoConvertidoId);
public sealed record PaginaCotacoesCorreios(IReadOnlyList<ResumoCotacaoCorreios> Itens, int TotalItens, int Pagina, int ItensPorPagina);
public sealed record FiltroCotacoesCorreios(int Pagina, int ItensPorPagina, SituacaoCotacaoCorreios? Situacao,
    DateTimeOffset? CriadaDeUtc, DateTimeOffset? CriadaAteUtc);

public sealed record ComandoCotacaoCorreios(Guid Autor, bool Admin, Guid Chave, Guid? Id,
    AcaoCotacaoCorreios Acao, int? Versao = null, SolicitarCotacaoCorreios? Solicitacao = null,
    PublicarPropostaCorreios? Proposta = null, DecidirCotacaoCorreios? Decisao = null);
public sealed record ResultadoCotacaoCorreios(int Status, string? Codigo = null, ResumoCotacaoCorreios? Cotacao = null,
    int? Versao = null);

public interface ICotacoesCorreios
{
    Task<PaginaCotacoesCorreios> ListarAsync(Guid? titular, FiltroCotacoesCorreios filtro, CancellationToken ct);
    Task<DetalheCotacaoCorreios?> BuscarAsync(Guid? titular, Guid id, CancellationToken ct);
    Task<ResultadoCotacaoCorreios> ExecutarAsync(ComandoCotacaoCorreios comando, CancellationToken ct);
}
