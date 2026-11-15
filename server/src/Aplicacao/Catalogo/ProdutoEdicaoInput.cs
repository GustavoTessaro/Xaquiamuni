namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoEdicaoInput(
    string? Nome,
    string? Descricao,
    Guid? CategoriaId);
