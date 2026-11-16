namespace Xaquiamuni.Dominio.Estoque;

public sealed record QuantidadeEstoque
{
    public const int Precision = 18;
    public const int Scale = 6;
    public const decimal MaximoNaUnidadeBase = 999_999_999_999.999999m;

    private QuantidadeEstoque(
        decimal valorBase,
        UnidadeFisicaEstoque unidadeBase,
        DimensaoFisicaEstoque dimensao)
    {
        ValorBase = valorBase;
        UnidadeBase = unidadeBase;
        Dimensao = dimensao;
    }

    public decimal ValorBase { get; }
    public UnidadeFisicaEstoque UnidadeBase { get; }
    public DimensaoFisicaEstoque Dimensao { get; }

    public static QuantidadeEstoque Criar(decimal quantidade, UnidadeFisicaEstoque unidade)
    {
        var definicao = ObterDefinicao(unidade);
        if (quantidade < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidade),
                "A quantidade de estoque não pode ser negativa.");
        }

        decimal valorBase;
        try
        {
            valorBase = checked(quantidade * definicao.FatorParaUnidadeBase);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidade),
                "A quantidade convertida excede o limite suportado.");
        }

        ValidarValorBase(valorBase, definicao.Dimensao);
        return new QuantidadeEstoque(valorBase, definicao.UnidadeBase, definicao.Dimensao);
    }

    public decimal ConverterPara(UnidadeFisicaEstoque unidadeDestino)
    {
        var definicaoDestino = ObterDefinicao(unidadeDestino);
        if (Dimensao != definicaoDestino.Dimensao)
        {
            throw new ArgumentException(
                "A conversão só é permitida entre unidades da mesma dimensão.",
                nameof(unidadeDestino));
        }

        return ValorBase / definicaoDestino.FatorParaUnidadeBase;
    }

    public QuantidadeEstoque ExigirPositiva(string paramName = "quantidade")
    {
        if (ValorBase == 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "A quantidade deve ser positiva neste contexto.");
        }

        return this;
    }

    private static void ValidarValorBase(decimal valorBase, DimensaoFisicaEstoque dimensao)
    {
        if (valorBase > MaximoNaUnidadeBase)
        {
            throw new ArgumentOutOfRangeException(
                "quantidade",
                $"A quantidade-base não pode exceder decimal({Precision},{Scale}).");
        }

        if (decimal.Round(valorBase, Scale, MidpointRounding.ToZero) != valorBase)
        {
            throw new ArgumentOutOfRangeException(
                "quantidade",
                $"A quantidade-base deve ser exata em até {Scale} casas decimais.");
        }

        if (dimensao == DimensaoFisicaEstoque.Contagem && decimal.Truncate(valorBase) != valorBase)
        {
            throw new ArgumentOutOfRangeException(
                "quantidade",
                "A dimensão de contagem não permite frações de unidade.");
        }
    }

    private static DefinicaoUnidade ObterDefinicao(UnidadeFisicaEstoque unidade) =>
        unidade switch
        {
            UnidadeFisicaEstoque.Grama =>
                new DefinicaoUnidade(DimensaoFisicaEstoque.Massa, UnidadeFisicaEstoque.Grama, 1m),
            UnidadeFisicaEstoque.Quilograma =>
                new DefinicaoUnidade(DimensaoFisicaEstoque.Massa, UnidadeFisicaEstoque.Grama, 1000m),
            UnidadeFisicaEstoque.Mililitro =>
                new DefinicaoUnidade(DimensaoFisicaEstoque.Volume, UnidadeFisicaEstoque.Mililitro, 1m),
            UnidadeFisicaEstoque.Litro =>
                new DefinicaoUnidade(DimensaoFisicaEstoque.Volume, UnidadeFisicaEstoque.Mililitro, 1000m),
            UnidadeFisicaEstoque.Unidade =>
                new DefinicaoUnidade(DimensaoFisicaEstoque.Contagem, UnidadeFisicaEstoque.Unidade, 1m),
            _ => throw new ArgumentOutOfRangeException(nameof(unidade), unidade, "Unidade física inválida.")
        };

    private readonly record struct DefinicaoUnidade(
        DimensaoFisicaEstoque Dimensao,
        UnidadeFisicaEstoque UnidadeBase,
        decimal FatorParaUnidadeBase);
}
