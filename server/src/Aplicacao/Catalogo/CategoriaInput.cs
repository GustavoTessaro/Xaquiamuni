namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record CategoriaInput(
    string? Nome,
    string? Descricao,
    int? OrdemExibicao, Guid? VersaoEsperada = null);
