namespace Xaquiamuni.Aplicacao.Financeiro;

public sealed record ResumoFinanceiroDto(
    FluxoFinanceiroDto FluxoPeriodo,
    PosicaoFinanceiraDto PosicaoAtual);

public sealed record FluxoFinanceiroDto(DateOnly? DataInicial, DateOnly? DataFinal,
    string EntradasRecebidasCentavos, string SaidasPagasCentavos, string MovimentacaoLiquidaCentavos);

public sealed record PosicaoFinanceiraDto(string AReceberCentavos, string APagarCentavos, long ComprasValorAConferir);
