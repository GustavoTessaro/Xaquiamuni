namespace Xaquiamuni.Aplicacao.Imagens;

public static class ChaveArmazenamentoImagemProduto
{
    public static string Gerar() => $"product-images/{Guid.NewGuid():N}.webp";

    public static bool EhValida(string? chave)
    {
        const string prefixo = "product-images/";
        const string sufixo = ".webp";

        if (chave is null
            || !chave.StartsWith(prefixo, StringComparison.Ordinal)
            || !chave.EndsWith(sufixo, StringComparison.Ordinal))
        {
            return false;
        }

        var identificador = chave[prefixo.Length..^sufixo.Length];
        return Guid.TryParseExact(identificador, "N", out _);
    }
}
