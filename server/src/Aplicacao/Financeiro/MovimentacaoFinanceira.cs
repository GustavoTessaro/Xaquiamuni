namespace Xaquiamuni.Aplicacao.Financeiro;

public enum TipoMovimentacaoFinanceira { Entrada = 1, Saida }
public enum OrigemMovimentacaoFinanceira { PedidoSite = 1, VendaExterna, PagamentoCompra, LancamentoManual }

public sealed record FiltroMovimentacoesFinanceiras(DateOnly? DataInicial, DateOnly? DataFinal,
    TipoMovimentacaoFinanceira? Tipo, OrigemMovimentacaoFinanceira? Origem, int Pagina)
{
    public const int TamanhoPagina = 50;
    public bool Valido => Pagina is >= 1 and <= 10000 &&
        (!DataInicial.HasValue || !DataFinal.HasValue || DataInicial <= DataFinal) &&
        (!Tipo.HasValue || Enum.IsDefined(Tipo.Value)) && (!Origem.HasValue || Enum.IsDefined(Origem.Value));
}

public sealed record MovimentacaoFinanceiraDto(Guid EventoId, Guid RecursoId, string Tipo,
    string Origem, DateOnly Data, string NaturezaData, string ValorCentavos, string Descricao, string? Meio);

public sealed record PaginaMovimentacoesFinanceiras(int Pagina, int TamanhoPagina, bool TemProxima,
    IReadOnlyList<MovimentacaoFinanceiraDto> Itens);
