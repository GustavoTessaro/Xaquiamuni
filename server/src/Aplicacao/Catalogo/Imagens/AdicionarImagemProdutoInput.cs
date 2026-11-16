namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public sealed record AdicionarImagemProdutoInput(
    Stream? Arquivo,
    string? NomeOriginal,
    IReadOnlyCollection<Guid>? VarianteIds = null);
