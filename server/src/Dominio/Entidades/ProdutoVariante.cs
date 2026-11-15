using System.Text;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class ProdutoVariante
{
    public const int IdentificacaoMaxLength = 100;
    public const int UnidadeVendaMaxLength = 10;
    private const decimal PrecoVendaMaximo = 9999999999999999.99m;

    private ProdutoVariante()
    {
    }

    private ProdutoVariante(
        Guid id,
        string identificacao,
        string identificacaoNormalizada,
        string unidadeVenda,
        decimal precoVenda,
        bool ativa,
        bool padrao)
    {
        Id = id;
        Identificacao = identificacao;
        IdentificacaoNormalizada = identificacaoNormalizada;
        UnidadeVenda = unidadeVenda;
        PrecoVenda = precoVenda;
        Ativa = ativa;
        Padrao = padrao;
    }

    public Guid Id { get; private set; }
    public Guid ProdutoId { get; private set; }
    public string Identificacao { get; private set; } = null!;
    public string IdentificacaoNormalizada { get; private set; } = null!;
    public string UnidadeVenda { get; private set; } = null!;
    public decimal PrecoVenda { get; private set; }
    public bool Ativa { get; private set; }
    public bool Padrao { get; private set; }
    public Produto Produto { get; private set; } = null!;

    public static ProdutoVariante Criar(
        string? identificacao,
        string? unidadeVenda,
        decimal precoVenda,
        bool ativa = true,
        bool padrao = false)
    {
        var identificacaoNormalizada = NormalizarIdentificacao(identificacao);
        var unidadeVendaNormalizada = NormalizarUnidadeVenda(unidadeVenda);
        ValidarPreco(precoVenda);
        if (padrao && !ativa)
        {
            throw new ArgumentException("A variante padrão deve estar ativa.", nameof(ativa));
        }

        return new ProdutoVariante(
            Guid.NewGuid(),
            identificacaoNormalizada,
            identificacaoNormalizada.ToUpperInvariant(),
            unidadeVendaNormalizada,
            precoVenda,
            ativa,
            padrao);
    }

    internal void AssociarA(Produto produto)
    {
        ArgumentNullException.ThrowIfNull(produto);
        if (ProdutoId != Guid.Empty)
        {
            throw new InvalidOperationException("A variante já pertence a um produto.");
        }

        ProdutoId = produto.Id;
        Produto = produto;
    }

    internal void DefinirPadrao(bool padrao)
    {
        if (padrao && !Ativa)
        {
            throw new InvalidOperationException("Uma variante inativa não pode ser padrão.");
        }

        Padrao = padrao;
    }

    internal void DefinirAtiva(bool ativa)
    {
        if (!ativa && Padrao)
        {
            throw new InvalidOperationException("A variante padrão não pode ser desativada diretamente.");
        }

        Ativa = ativa;
    }

    private static string NormalizarIdentificacao(string? identificacao)
    {
        if (string.IsNullOrWhiteSpace(identificacao))
        {
            throw new ArgumentException("A identificação da variante é obrigatória.", nameof(identificacao));
        }

        var normalized = NormalizarEspacos(identificacao);
        if (normalized.Length > IdentificacaoMaxLength)
        {
            throw new ArgumentException(
                $"A identificação da variante não pode exceder {IdentificacaoMaxLength} caracteres.",
                nameof(identificacao));
        }

        return normalized;
    }

    private static string NormalizarUnidadeVenda(string? unidadeVenda)
    {
        if (string.IsNullOrWhiteSpace(unidadeVenda))
        {
            throw new ArgumentException("A unidade de venda é obrigatória.", nameof(unidadeVenda));
        }

        var normalized = unidadeVenda.Trim().ToUpperInvariant();
        if (normalized.Length > UnidadeVendaMaxLength)
        {
            throw new ArgumentException(
                $"A unidade de venda não pode exceder {UnidadeVendaMaxLength} caracteres.",
                nameof(unidadeVenda));
        }

        return normalized;
    }

    private static void ValidarPreco(decimal precoVenda)
    {
        if (precoVenda <= 0
            || precoVenda > PrecoVendaMaximo
            || decimal.Round(precoVenda, 2) != precoVenda)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precoVenda),
                "O preço de venda deve ser positivo e ter no máximo duas casas decimais.");
        }
    }

    private static string NormalizarEspacos(string value)
    {
        var normalized = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(character);
        }

        return normalized.ToString();
    }
}
