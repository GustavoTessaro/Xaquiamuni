namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoVarianteInput(
    string? Identificacao,
    string? UnidadeVenda,
    decimal PrecoVenda);
