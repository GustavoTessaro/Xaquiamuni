namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoCadastroInput(
    string? Nome,
    string? Descricao,
    Guid? CategoriaId,
    ProdutoVarianteInput? VariantePadrao,
    IReadOnlyList<ProdutoVarianteInput?>? Variantes = null);
