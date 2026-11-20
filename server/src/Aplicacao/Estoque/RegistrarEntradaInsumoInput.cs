using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record RegistrarEntradaInsumoInput(
    Guid InsumoId,
    Guid ResponsavelId,
    TipoMovimentoInsumo Tipo,
    decimal QuantidadeOriginal,
    UnidadeFisicaEstoque? UnidadeOriginal,
    Guid? FormaCompraId,
    Guid? LoteId,
    string? IdentificacaoNovoLote,
    DateOnly? DataEntradaNovoLote,
    DateOnly? DataValidadeNovoLote,
    string? FornecedorNovoLote,
    bool NovoLoteBloqueado,
    decimal? CustoTotal,
    string ChaveIdempotencia,
    string? Observacao = null,
    bool SemControleValidade = false);
