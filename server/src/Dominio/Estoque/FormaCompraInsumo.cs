using System.Text;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class FormaCompraInsumo
{
    public const int NomeMaxLength = 100;

    private FormaCompraInsumo(
        Guid id,
        Guid insumoId,
        string nome,
        string nomeNormalizado,
        QuantidadeEstoque quantidadeEquivalentePorEmbalagem)
    {
        Id = id;
        InsumoId = insumoId;
        Nome = nome;
        NomeNormalizado = nomeNormalizado;
        QuantidadeEquivalentePorEmbalagem = quantidadeEquivalentePorEmbalagem;
        Ativa = true;
    }

    private FormaCompraInsumo()
    {
    }

    public Guid Id { get; private set; }
    public Guid InsumoId { get; private set; }
    public string Nome { get; private set; } = null!;
    public string NomeNormalizado { get; private set; } = null!;
    public QuantidadeEstoque QuantidadeEquivalentePorEmbalagem { get; private set; } = null!;
    public bool Ativa { get; private set; }

    internal static FormaCompraInsumo Criar(
        Guid insumoId,
        string? nome,
        decimal quantidadeEquivalente,
        UnidadeFisicaEstoque unidade)
    {
        var nomeNormalizado = NormalizarNome(nome);
        var fator = QuantidadeEstoque.Criar(quantidadeEquivalente, unidade).ExigirPositiva(
            nameof(quantidadeEquivalente));

        return new FormaCompraInsumo(
            Guid.NewGuid(),
            insumoId,
            nomeNormalizado,
            nomeNormalizado.ToUpperInvariant(),
            fator);
    }

    internal static string NormalizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O nome da forma de compra é obrigatório.", nameof(nome));
        }

        var normalized = NormalizarEspacos(nome);
        if (normalized.Length > NomeMaxLength)
        {
            throw new ArgumentException(
                $"O nome da forma de compra não pode exceder {NomeMaxLength} caracteres.",
                nameof(nome));
        }

        return normalized;
    }

    internal void AtualizarDados(string nome, string nomeNormalizado, QuantidadeEstoque fator)
    {
        Nome = nome;
        NomeNormalizado = nomeNormalizado;
        QuantidadeEquivalentePorEmbalagem = fator;
    }

    internal void DefinirAtiva(bool ativa)
    {
        Ativa = ativa;
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
