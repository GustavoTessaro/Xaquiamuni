namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoFiltro(
    string? Busca = null,
    Guid? CategoriaId = null,
    bool? Publicado = null);
