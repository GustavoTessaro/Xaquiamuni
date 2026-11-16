namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record EstornarMovimentoInsumoInput(
    Guid MovimentoOriginalId,
    Guid ResponsavelId,
    string Motivo,
    string ChaveIdempotencia,
    string? Observacao = null);
