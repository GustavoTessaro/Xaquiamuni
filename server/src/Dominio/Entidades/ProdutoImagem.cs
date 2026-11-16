namespace Xaquiamuni.Dominio.Entidades;

public sealed class ProdutoImagem
{
    public const int ChaveArmazenamentoMaxLength = 512;
    public const int NomeOriginalMaxLength = 255;
    public const int TipoConteudoMaxLength = 100;
    public const int QuantidadeMaximaPorProduto = 5;

    private readonly List<ProdutoImagemVariante> _variantes = [];

    private ProdutoImagem()
    {
    }

    private ProdutoImagem(
        Guid id,
        Guid produtoId,
        string chaveArmazenamento,
        string nomeOriginal,
        string tipoConteudo,
        long tamanhoBytes,
        int larguraPixels,
        int alturaPixels,
        int ordemExibicao,
        int slotGaleria,
        bool principal,
        DateTimeOffset criadoEmUtc,
        Produto produto)
    {
        Id = id;
        ProdutoId = produtoId;
        ChaveArmazenamento = chaveArmazenamento;
        NomeOriginal = nomeOriginal;
        TipoConteudo = tipoConteudo;
        TamanhoBytes = tamanhoBytes;
        LarguraPixels = larguraPixels;
        AlturaPixels = alturaPixels;
        OrdemExibicao = ordemExibicao;
        SlotGaleria = slotGaleria;
        Principal = principal;
        CriadoEmUtc = criadoEmUtc;
        Produto = produto;
    }

    public Guid Id { get; private set; }
    public Guid ProdutoId { get; private set; }
    public string ChaveArmazenamento { get; private set; } = null!;
    public string NomeOriginal { get; private set; } = null!;
    public string TipoConteudo { get; private set; } = null!;
    public long TamanhoBytes { get; private set; }
    public int LarguraPixels { get; private set; }
    public int AlturaPixels { get; private set; }
    public int OrdemExibicao { get; private set; }
    public int SlotGaleria { get; private set; }
    public bool Principal { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public Produto Produto { get; private set; } = null!;
    public IReadOnlyCollection<ProdutoImagemVariante> Variantes => _variantes.AsReadOnly();

    internal static ProdutoImagem Criar(
        Produto produto,
        string? chaveArmazenamento,
        string? nomeOriginal,
        string? tipoConteudo,
        long tamanhoBytes,
        int larguraPixels,
        int alturaPixels,
        int ordemExibicao,
        int slotGaleria,
        bool principal,
        DateTimeOffset? criadoEmUtc = null)
    {
        ArgumentNullException.ThrowIfNull(produto);
        var storageKey = ValidarChaveArmazenamento(chaveArmazenamento);
        var originalName = ValidarNomeOriginal(nomeOriginal);
        var contentType = ValidarTipoConteudo(tipoConteudo);
        if (tamanhoBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tamanhoBytes),
                "O tamanho da imagem deve ser positivo.");
        }

        if (larguraPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(larguraPixels),
                "A largura da imagem deve ser positiva.");
        }

        if (alturaPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alturaPixels),
                "A altura da imagem deve ser positiva.");
        }

        if (ordemExibicao is < 1 or > QuantidadeMaximaPorProduto)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordemExibicao),
                "A ordem da imagem deve estar entre 1 e 5.");
        }

        if (slotGaleria is < 1 or > QuantidadeMaximaPorProduto)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotGaleria),
                "A vaga da imagem deve estar entre 1 e 5.");
        }

        var creationTime = criadoEmUtc ?? DateTimeOffset.UtcNow;
        if (creationTime.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A data de criação da imagem deve estar em UTC.",
                nameof(criadoEmUtc));
        }

        return new ProdutoImagem(
            Guid.NewGuid(),
            produto.Id,
            storageKey,
            originalName,
            contentType,
            tamanhoBytes,
            larguraPixels,
            alturaPixels,
            ordemExibicao,
            slotGaleria,
            principal,
            creationTime,
            produto);
    }

    internal void DefinirOrdemExibicao(int ordemExibicao)
    {
        if (ordemExibicao is < 1 or > QuantidadeMaximaPorProduto)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordemExibicao),
                "A ordem da imagem deve estar entre 1 e 5.");
        }

        OrdemExibicao = ordemExibicao;
    }

    internal void DefinirPrincipal(bool principal) => Principal = principal;

    internal ProdutoImagemVariante AssociarVariante(ProdutoVariante variante)
    {
        ArgumentNullException.ThrowIfNull(variante);
        if (variante.ProdutoId != ProdutoId)
        {
            throw new ArgumentException(
                "A variante deve pertencer ao mesmo produto da imagem.",
                nameof(variante));
        }

        if (_variantes.Any(item => item.ProdutoVarianteId == variante.Id))
        {
            throw new InvalidOperationException(
                "A imagem já está vinculada a esta opção do produto.");
        }

        var association = ProdutoImagemVariante.Criar(this, variante);
        _variantes.Add(association);
        return association;
    }

    internal bool DesassociarVariante(Guid varianteId)
    {
        var association = _variantes.SingleOrDefault(item => item.ProdutoVarianteId == varianteId);
        return association is not null && _variantes.Remove(association);
    }

    private static string ValidarChaveArmazenamento(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A chave de armazenamento é obrigatória.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > ChaveArmazenamentoMaxLength)
        {
            throw new ArgumentException(
                $"A chave de armazenamento não pode exceder {ChaveArmazenamentoMaxLength} caracteres.",
                nameof(value));
        }

        if (normalized.StartsWith('/') || normalized.Contains('\\')
            || normalized.Split('/').Any(segment => segment is "." or "..")
            || Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "A chave de armazenamento deve ser uma referência relativa, não um caminho ou URL.",
                nameof(value));
        }

        return normalized;
    }

    public static string ValidarNomeOriginal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O nome original da imagem é obrigatório.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > NomeOriginalMaxLength)
        {
            throw new ArgumentException(
                $"O nome original não pode exceder {NomeOriginalMaxLength} caracteres.",
                nameof(value));
        }

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "O nome original da imagem não pode conter caracteres de controle.",
                nameof(value));
        }

        return normalized;
    }

    private static string ValidarTipoConteudo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O tipo de conteúdo da imagem é obrigatório.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > TipoConteudoMaxLength)
        {
            throw new ArgumentException(
                $"O tipo de conteúdo não pode exceder {TipoConteudoMaxLength} caracteres.",
                nameof(value));
        }

        return normalized;
    }
}
