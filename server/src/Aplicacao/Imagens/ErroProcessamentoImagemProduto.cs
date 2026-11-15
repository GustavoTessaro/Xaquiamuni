namespace Xaquiamuni.Aplicacao.Imagens;

public enum ErroProcessamentoImagemProduto
{
    ArquivoMuitoGrande,
    ImagemInvalida,
    FormatoNaoSuportado,
    ImagemAnimada,
    DimensoesExcessivas,
    FalhaNaDecodificacao,
    FalhaNaCodificacao
}

public sealed class ProcessamentoImagemProdutoException : Exception
{
    public ProcessamentoImagemProdutoException(
        ErroProcessamentoImagemProduto erro,
        string message)
        : base(message)
    {
        Erro = erro;
    }

    public ErroProcessamentoImagemProduto Erro { get; }
}
