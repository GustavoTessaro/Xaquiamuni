namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoCaracteristicaInput(
    string? Referencia,
    Guid? Id,
    string? Nome,
    string? TipoMedidaFisica,
    IReadOnlyList<ProdutoCaracteristicaValorInput?>? Valores = null,
    bool Remover = false);
