using System.Text;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class Categoria
{
    public const int NomeMaxLength = 100;
    public const int DescricaoMaxLength = 500;

    private Categoria()
    {
    }

    private Categoria(
        Guid id,
        string nome,
        string nomeNormalizado,
        string? descricao,
        bool ativa,
        int ordemExibicao)
    {
        Id = id;
        Nome = nome;
        NomeNormalizado = nomeNormalizado;
        Descricao = descricao;
        Ativa = ativa;
        OrdemExibicao = ordemExibicao;
    }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = null!;
    public string NomeNormalizado { get; private set; } = null!;
    public string? Descricao { get; private set; }
    public bool Ativa { get; private set; }
    public int OrdemExibicao { get; private set; }

    public static Categoria Criar(string? nome, string? descricao, int? ordemExibicao)
    {
        var nomeNormalizado = NormalizarNome(nome);
        var descricaoNormalizada = NormalizarDescricao(descricao);
        var ordem = ValidarOrdem(ordemExibicao);

        return new Categoria(
            Guid.NewGuid(),
            nomeNormalizado,
            nomeNormalizado.ToUpperInvariant(),
            descricaoNormalizada,
            ativa: true,
            ordem);
    }

    public void AtualizarDados(string? nome, string? descricao, int? ordemExibicao)
    {
        var nomeNormalizado = NormalizarNome(nome);
        var descricaoNormalizada = NormalizarDescricao(descricao);
        var ordem = ValidarOrdem(ordemExibicao);

        Nome = nomeNormalizado;
        NomeNormalizado = nomeNormalizado.ToUpperInvariant();
        Descricao = descricaoNormalizada;
        OrdemExibicao = ordem;
    }

    public void DefinirAtiva(bool ativa)
    {
        Ativa = ativa;
    }

    public override string ToString() => "[Categoria]";

    private static string NormalizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O nome da categoria é obrigatório.", nameof(nome));
        }

        var normalized = NormalizarEspacos(nome);
        if (normalized.Length > NomeMaxLength)
        {
            throw new ArgumentException(
                $"O nome da categoria não pode exceder {NomeMaxLength} caracteres.",
                nameof(nome));
        }

        return normalized;
    }

    private static string? NormalizarDescricao(string? descricao)
    {
        if (string.IsNullOrWhiteSpace(descricao))
        {
            return null;
        }

        var normalized = NormalizarEspacos(descricao);
        if (normalized.Length > DescricaoMaxLength)
        {
            throw new ArgumentException(
                $"A descrição da categoria não pode exceder {DescricaoMaxLength} caracteres.",
                nameof(descricao));
        }

        return normalized;
    }

    private static int ValidarOrdem(int? ordemExibicao)
    {
        if (ordemExibicao is null or < 0)
        {
            throw new ArgumentException(
                "A ordem de exibição deve ser um número inteiro igual ou maior que zero.",
                nameof(ordemExibicao));
        }

        return ordemExibicao.Value;
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
