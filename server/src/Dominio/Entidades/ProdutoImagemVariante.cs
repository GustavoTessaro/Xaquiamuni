namespace Xaquiamuni.Dominio.Entidades;

public sealed class ProdutoImagemVariante
{
    private ProdutoImagemVariante()
    {
    }

    private ProdutoImagemVariante(ProdutoImagem imagem, ProdutoVariante variante)
    {
        ProdutoId = imagem.ProdutoId;
        ProdutoImagemId = imagem.Id;
        ProdutoVarianteId = variante.Id;
        Imagem = imagem;
        Variante = variante;
    }

    public Guid ProdutoId { get; private set; }
    public Guid ProdutoImagemId { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public ProdutoImagem Imagem { get; private set; } = null!;
    public ProdutoVariante Variante { get; private set; } = null!;

    internal static ProdutoImagemVariante Criar(ProdutoImagem imagem, ProdutoVariante variante)
    {
        ArgumentNullException.ThrowIfNull(imagem);
        ArgumentNullException.ThrowIfNull(variante);
        if (imagem.ProdutoId != variante.ProdutoId)
        {
            throw new ArgumentException(
                "A imagem e a variante devem pertencer ao mesmo produto.",
                nameof(variante));
        }

        return new ProdutoImagemVariante(imagem, variante);
    }
}
