namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public sealed record ProdutoImagemDto(
    Guid Id,
    Guid ProdutoId,
    string NomeOriginal,
    string TipoConteudo,
    long TamanhoBytes,
    int LarguraPixels,
    int AlturaPixels,
    int OrdemExibicao,
    bool Principal,
    IReadOnlyList<Guid> VarianteIds,
    DateTimeOffset CriadoEmUtc);
