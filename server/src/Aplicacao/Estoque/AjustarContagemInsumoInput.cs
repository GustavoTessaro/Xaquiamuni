using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record AjustarContagemInsumoInput(
    Guid InsumoId,
    Guid ResponsavelId,
    decimal QuantidadeEncontrada,
    UnidadeFisicaEstoque UnidadeOriginal,
    Guid? LoteId,
    byte[]? VersaoSaldoEsperada,
    string Motivo,
    string ChaveIdempotencia,
    string? Observacao = null);
