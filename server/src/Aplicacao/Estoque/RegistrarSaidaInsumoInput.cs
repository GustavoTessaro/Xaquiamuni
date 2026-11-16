using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record RegistrarSaidaInsumoInput(
    Guid InsumoId,
    Guid ResponsavelId,
    TipoMovimentoInsumo Tipo,
    decimal QuantidadeOriginal,
    UnidadeFisicaEstoque UnidadeOriginal,
    Guid? LoteId,
    string Motivo,
    string ChaveIdempotencia,
    string? Observacao = null);
