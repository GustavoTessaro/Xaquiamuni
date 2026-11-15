using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class ProdutoVariante
{
    public const int IdentificacaoMaxLength = 100;
    public const int UnidadeVendaMaxLength = UnidadeVendaCatalogo.CodigoMaxLength;
    private const decimal PrecoVendaMaximo = 9999999999999999.99m;

    private readonly List<ProdutoVarianteCaracteristicaValor> _caracteristicaValores = [];
    private decimal? _pesoLiquidoGramas;
    private decimal? _volumeLiquidoMililitros;
    private byte[]? _combinacaoHash;

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
        bool padrao,
        decimal? pesoLiquidoGramas,
        decimal? volumeLiquidoMililitros)
    {
        Id = id;
        Identificacao = identificacao;
        IdentificacaoNormalizada = identificacaoNormalizada;
        UnidadeVenda = unidadeVenda;
        PrecoVenda = precoVenda;
        Ativa = ativa;
        Padrao = padrao;
        _pesoLiquidoGramas = pesoLiquidoGramas;
        _volumeLiquidoMililitros = volumeLiquidoMililitros;
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
    public IReadOnlyCollection<ProdutoVarianteCaracteristicaValor> CaracteristicaValores =>
        _caracteristicaValores.AsReadOnly();

    [NotMapped]
    public decimal? PesoLiquidoGramas =>
        ObterMedidaCaracteristica(TipoMedidaFisica.PesoLiquido) ?? _pesoLiquidoGramas;

    [NotMapped]
    public decimal? VolumeLiquidoMililitros =>
        ObterMedidaCaracteristica(TipoMedidaFisica.VolumeLiquido) ?? _volumeLiquidoMililitros;

    public static ProdutoVariante Criar(
        string? identificacao,
        string? unidadeVenda,
        decimal precoVenda,
        bool ativa = true,
        bool padrao = false,
        decimal? pesoLiquidoGramas = null,
        decimal? volumeLiquidoMililitros = null)
    {
        var identificacaoNormalizada = NormalizarIdentificacao(identificacao);
        var unidadeVendaNormalizada = NormalizarUnidadeVenda(unidadeVenda);
        ValidarPreco(precoVenda);
        var peso = MedidaFisicaCatalogo.ValidarPesoCanonico(pesoLiquidoGramas);
        var volume = MedidaFisicaCatalogo.ValidarVolumeCanonico(volumeLiquidoMililitros);
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
            padrao,
            peso,
            volume);
    }

    public void AtualizarDados(string? identificacao, string? unidadeVenda, decimal precoVenda)
    {
        var normalizedIdentification = NormalizarIdentificacao(identificacao);
        var normalizedUnit = NormalizarUnidadeVendaPreservandoLegado(unidadeVenda);
        ValidarPreco(precoVenda);

        Identificacao = normalizedIdentification;
        IdentificacaoNormalizada = normalizedIdentification.ToUpperInvariant();
        UnidadeVenda = normalizedUnit;
        PrecoVenda = precoVenda;
    }

    public void DefinirMedidasLiquidas(decimal? pesoLiquidoGramas, decimal? volumeLiquidoMililitros)
    {
        var peso = MedidaFisicaCatalogo.ValidarPesoCanonico(pesoLiquidoGramas);
        var volume = MedidaFisicaCatalogo.ValidarVolumeCanonico(volumeLiquidoMililitros);
        var pesoSelecionado = ObterMedidaCaracteristica(TipoMedidaFisica.PesoLiquido);
        var volumeSelecionado = ObterMedidaCaracteristica(TipoMedidaFisica.VolumeLiquido);

        if (pesoSelecionado is not null && peso != pesoSelecionado
            || volumeSelecionado is not null && volume != volumeSelecionado)
        {
            throw new InvalidOperationException(
                "A medida física vinculada a uma característica não pode divergir do valor selecionado.");
        }

        _pesoLiquidoGramas = pesoSelecionado is null ? peso : null;
        _volumeLiquidoMililitros = volumeSelecionado is null ? volume : null;
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

    internal void AssociarCaracteristicaValor(
        ProdutoVarianteCaracteristicaValor associacao,
        byte[]? combinacaoHash)
    {
        ArgumentNullException.ThrowIfNull(associacao);
        if (associacao.ProdutoId != ProdutoId || associacao.ProdutoVarianteId != Id)
        {
            throw new ArgumentException(
                "A associação deve pertencer à variante e ao produto.",
                nameof(associacao));
        }

        if (_caracteristicaValores.Any(item =>
                item.ProdutoCaracteristicaId == associacao.ProdutoCaracteristicaId))
        {
            throw new InvalidOperationException(
                "A variante já possui um valor para esta característica.");
        }

        ValidarMedidaAssociada(associacao);
        _caracteristicaValores.Add(associacao);
        LimparMedidaDiretaDerivada(associacao);
        DefinirCombinacaoHash(combinacaoHash);
    }

    internal void SubstituirCaracteristicaValores(
        IReadOnlyCollection<ProdutoVarianteCaracteristicaValor> associacoes,
        byte[]? combinacaoHash)
    {
        ArgumentNullException.ThrowIfNull(associacoes);
        if (associacoes.Any(associacao =>
                associacao.ProdutoId != ProdutoId || associacao.ProdutoVarianteId != Id))
        {
            throw new ArgumentException(
                "Todas as associações devem pertencer à variante e ao produto.",
                nameof(associacoes));
        }

        if (associacoes.Select(associacao => associacao.ProdutoCaracteristicaId)
            .Distinct()
            .Count() != associacoes.Count)
        {
            throw new InvalidOperationException(
                "A variante pode possuir somente um valor por característica.");
        }

        foreach (var associacao in associacoes)
        {
            ValidarMedidaAssociada(associacao);
        }

        _caracteristicaValores.Clear();
        _caracteristicaValores.AddRange(associacoes);
        foreach (var associacao in associacoes)
        {
            LimparMedidaDiretaDerivada(associacao);
        }

        DefinirCombinacaoHash(combinacaoHash);
    }

    internal void DefinirCombinacaoHash(byte[]? combinacaoHash)
    {
        if (combinacaoHash is not null && combinacaoHash.Length != 32)
        {
            throw new ArgumentException("A assinatura da combinação deve conter 32 bytes.", nameof(combinacaoHash));
        }

        _combinacaoHash = combinacaoHash?.ToArray();
    }

    internal bool TemCombinacao(byte[] combinacaoHash) =>
        _combinacaoHash is not null && _combinacaoHash.AsSpan().SequenceEqual(combinacaoHash);

    private decimal? ObterMedidaCaracteristica(TipoMedidaFisica tipo) =>
        _caracteristicaValores
            .SingleOrDefault(item => item.Caracteristica.TipoMedidaFisica == tipo)
            ?.Valor.ValorMedidaCanonico;

    private void ValidarMedidaAssociada(ProdutoVarianteCaracteristicaValor associacao)
    {
        var valorMedida = associacao.Valor.ValorMedidaCanonico;
        switch (associacao.Caracteristica.TipoMedidaFisica)
        {
            case TipoMedidaFisica.PesoLiquido when _pesoLiquidoGramas is not null
                && _pesoLiquidoGramas != valorMedida:
                throw new InvalidOperationException(
                    "A medida de peso existente diverge do valor da característica.");
            case TipoMedidaFisica.VolumeLiquido when _volumeLiquidoMililitros is not null
                && _volumeLiquidoMililitros != valorMedida:
                throw new InvalidOperationException(
                    "A medida de volume existente diverge do valor da característica.");
        }
    }

    private void LimparMedidaDiretaDerivada(ProdutoVarianteCaracteristicaValor associacao)
    {
        switch (associacao.Caracteristica.TipoMedidaFisica)
        {
            case TipoMedidaFisica.PesoLiquido:
                _pesoLiquidoGramas = null;
                break;
            case TipoMedidaFisica.VolumeLiquido:
                _volumeLiquidoMililitros = null;
                break;
        }
    }

    private string NormalizarUnidadeVendaPreservandoLegado(string? unidadeVenda)
    {
        if (string.IsNullOrWhiteSpace(unidadeVenda))
        {
            throw new ArgumentException("A unidade de venda é obrigatória.", nameof(unidadeVenda));
        }

        var normalized = unidadeVenda.Trim().ToUpperInvariant();
        return string.Equals(normalized, UnidadeVenda, StringComparison.Ordinal)
            ? UnidadeVenda
            : NormalizarUnidadeVenda(normalized);
    }

    internal static string NormalizarIdentificacao(string? identificacao)
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
        => UnidadeVendaCatalogo.Normalizar(unidadeVenda);

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
