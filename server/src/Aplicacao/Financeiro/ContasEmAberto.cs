namespace Xaquiamuni.Aplicacao.Financeiro;

public enum DirecaoConta { APagar, AReceber }
public enum OrigemConta { Manual, Compra, VendaExterna }
public enum SituacaoVencimento { EmAberto, Vencida, VenceHoje, VencendoEmBreve }
public enum FiltroVencimento { Todas, Vencidas, VencendoEmBreve, VenceHoje, ProximosTresDias }

public sealed record FiltroContasEmAberto(DirecaoConta? Direcao, FiltroVencimento Classificacao, OrigemConta? Origem, int Pagina)
{
    public const int TamanhoPagina = 50;
}

public sealed record ContaEmAbertoDto(Guid Id, string Direcao, string Origem, string Descricao,
    string? Contraparte, string ValorPendenteCentavos, DateOnly? Vencimento, string Situacao, string ReferenciaApi);

public sealed record PaginaContasEmAberto(DateOnly DataOperacional, int Pagina, int TamanhoPagina, bool TemProxima,
    IReadOnlyList<ContaEmAbertoDto> Itens);

public sealed record TotalContasDto(long Quantidade, string ValorCentavos);
public sealed record IndicadoresDirecaoContaDto(long Quantidade, string ValorCentavos,
    TotalContasDto Vencidas, TotalContasDto VenceHoje, TotalContasDto VencendoEmBreve, TotalContasDto ProximosTresDias);
public sealed record IndicadoresContasEmAbertoDto(DateOnly DataOperacional, IndicadoresDirecaoContaDto APagar,
    IndicadoresDirecaoContaDto AReceber);
