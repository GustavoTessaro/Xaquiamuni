using System.Text;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class LoteInsumo
{
    public const int IdentificacaoMaxLength = 100;
    public const int FornecedorMaxLength = 200;

    private LoteInsumo(
        Guid id,
        Guid insumoId,
        string identificacao,
        DateOnly dataEntrada,
        DateOnly? dataValidade,
        string? fornecedor,
        bool bloqueado)
    {
        Id = id;
        InsumoId = insumoId;
        Identificacao = identificacao;
        DataEntrada = dataEntrada;
        DataValidade = dataValidade;
        Fornecedor = fornecedor;
        Bloqueado = bloqueado;
    }

    private LoteInsumo()
    {
    }

    public Guid Id { get; private set; }
    public Guid InsumoId { get; private set; }
    public string Identificacao { get; private set; } = null!;
    public DateOnly DataEntrada { get; private set; }
    public DateOnly? DataValidade { get; private set; }
    public string? Fornecedor { get; private set; }
    public bool Bloqueado { get; private set; }

    public static LoteInsumo Criar(
        InsumoEstoque insumo,
        string? identificacao,
        DateOnly dataEntrada,
        DateOnly? dataValidade = null,
        string? fornecedor = null,
        bool bloqueado = false)
    {
        ArgumentNullException.ThrowIfNull(insumo);

        if (!insumo.ControlaLote)
        {
            throw new ArgumentException(
                "Somente insumos configurados para controle por lote podem possuir lotes.",
                nameof(insumo));
        }

        var identificacaoNormalizada = NormalizarTextoObrigatorio(
            identificacao,
            IdentificacaoMaxLength,
            nameof(identificacao));
        var fornecedorNormalizado = NormalizarTextoOpcional(
            fornecedor,
            FornecedorMaxLength,
            nameof(fornecedor));

        if (insumo.ValidadeObrigatoria && dataValidade is null)
        {
            throw new ArgumentException(
                "Este insumo exige data de validade para cada lote.",
                nameof(dataValidade));
        }

        return new LoteInsumo(
            Guid.NewGuid(),
            insumo.Id,
            identificacaoNormalizada,
            dataEntrada,
            dataValidade,
            fornecedorNormalizado,
            bloqueado);
    }

    public void DefinirBloqueado(bool bloqueado)
    {
        Bloqueado = bloqueado;
    }

    public bool EstaElegivelEm(DateOnly dataReferencia) =>
        !Bloqueado && (DataValidade is null || DataValidade.Value >= dataReferencia);

    private static string NormalizarTextoObrigatorio(string? valor, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ArgumentException("O valor é obrigatório.", paramName);
        }

        var normalizado = NormalizarEspacos(valor);
        if (normalizado.Length > maxLength)
        {
            throw new ArgumentException($"O valor não pode exceder {maxLength} caracteres.", paramName);
        }

        return normalizado;
    }

    private static string? NormalizarTextoOpcional(string? valor, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var normalizado = NormalizarEspacos(valor);
        if (normalizado.Length > maxLength)
        {
            throw new ArgumentException($"O valor não pode exceder {maxLength} caracteres.", paramName);
        }

        return normalizado;
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
