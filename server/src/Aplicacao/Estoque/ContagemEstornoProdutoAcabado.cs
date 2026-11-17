using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record ContarProdutoAcabadoInput(
    Guid ProdutoVarianteId,
    Guid LoteId,
    Guid ResponsavelId,
    int QuantidadeVendavel,
    int QuantidadeDanificada,
    int QuantidadeQuarentena,
    string Motivo,
    string ChaveIdempotencia,
    byte[] VersaoSaldoEsperada,
    string? Observacao = null);

public sealed record ContarProdutoAcabadoResult(
    OperacaoProdutoAcabadoStatus Status,
    IReadOnlyList<MovimentoProdutoAcabado> Movimentos,
    string? Erro,
    bool Repetida,
    bool SemDiferenca)
{
    public bool Sucesso => Status == OperacaoProdutoAcabadoStatus.Sucesso
        || Status == OperacaoProdutoAcabadoStatus.SemDiferenca;

    public static ContarProdutoAcabadoResult Success(
        IReadOnlyList<MovimentoProdutoAcabado> movimentos,
        bool repetida = false) =>
        new(OperacaoProdutoAcabadoStatus.Sucesso, movimentos, null, repetida, false);

    public static ContarProdutoAcabadoResult NoDifference(
        bool repetida = false) =>
        new(OperacaoProdutoAcabadoStatus.SemDiferenca, [], null, repetida, true);

    public static ContarProdutoAcabadoResult Failure(
        OperacaoProdutoAcabadoStatus status,
        string erro) =>
        new(status, [], erro, false, false);
}

public sealed record EstornarMovimentoProdutoAcabadoInput(
    Guid MovimentoOriginalId,
    Guid ResponsavelId,
    string Motivo,
    string ChaveIdempotencia,
    string? Observacao = null);

public sealed record EstornarMovimentoProdutoAcabadoResult(
    OperacaoProdutoAcabadoStatus Status,
    MovimentoProdutoAcabado? Movimento,
    string? Erro,
    bool Repetida)
{
    public bool Sucesso => Status == OperacaoProdutoAcabadoStatus.Sucesso;

    public static EstornarMovimentoProdutoAcabadoResult Success(
        MovimentoProdutoAcabado movimento,
        bool repetida = false) =>
        new(OperacaoProdutoAcabadoStatus.Sucesso, movimento, null, repetida);

    public static EstornarMovimentoProdutoAcabadoResult Failure(
        OperacaoProdutoAcabadoStatus status,
        string erro) =>
        new(status, null, erro, false);
}
