namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoEdicaoInput(
    string? Nome,
    string? Descricao,
    Guid? CategoriaId,
    IReadOnlyList<ProdutoCaracteristicaInput?>? Caracteristicas = null,
    IReadOnlyList<ProdutoVarianteInput?>? Variantes = null);
