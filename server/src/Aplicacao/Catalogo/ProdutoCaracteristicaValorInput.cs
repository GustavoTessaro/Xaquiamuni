namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoCaracteristicaValorInput(
    string? Referencia,
    Guid? Id,
    string? Nome,
    decimal? ValorMedidaCanonico,
    bool Remover = false);
