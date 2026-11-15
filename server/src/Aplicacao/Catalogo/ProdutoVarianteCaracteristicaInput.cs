namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoVarianteCaracteristicaInput(
    Guid? CaracteristicaId,
    string? ReferenciaCaracteristica,
    Guid? ValorId,
    string? ReferenciaValor);
