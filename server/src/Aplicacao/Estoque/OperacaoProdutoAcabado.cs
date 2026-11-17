using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record RegistrarOperacaoProdutoAcabadoInput(
    Guid ProdutoVarianteId,
    Guid LoteId,
    Guid ResponsavelId,
    TipoMovimentoProdutoAcabado Tipo,
    int Quantidade,
    CondicaoEstoqueProdutoAcabado? CondicaoOrigem,
    CondicaoEstoqueProdutoAcabado? CondicaoDestino,
    string Motivo,
    string ChaveIdempotencia,
    string? Observacao = null);

public enum OperacaoProdutoAcabadoStatus : byte
{
    Sucesso = 1,
    SemDiferenca = 2,
    OperacaoInvalida = 3,
    VarianteNaoEncontrada = 4,
    LoteNaoEncontrado = 5,
    LoteDeOutraVariante = 6,
    LoteInelegivel = 7,
    VarianteInativa = 8,
    SaldoInsuficiente = 9,
    MovimentoNaoEncontrado = 10,
    MovimentoJaEstornado = 11,
    ConflitoIdempotencia = 12,
    ConflitoConcorrencia = 13
}

public sealed record OperacaoProdutoAcabadoResult(
    OperacaoProdutoAcabadoStatus Status,
    MovimentoProdutoAcabado? Movimento,
    string? Erro,
    bool Repetida)
{
    public bool Sucesso => Status == OperacaoProdutoAcabadoStatus.Sucesso;

    public static OperacaoProdutoAcabadoResult Success(
        MovimentoProdutoAcabado movimento,
        bool repetida = false) =>
        new(OperacaoProdutoAcabadoStatus.Sucesso, movimento, null, repetida);

    public static OperacaoProdutoAcabadoResult Failure(
        OperacaoProdutoAcabadoStatus status,
        string erro) =>
        new(status, null, erro, false);
}

public interface IRegistrarOperacaoProdutoAcabado
{
    Task<OperacaoProdutoAcabadoResult> ExecuteAsync(
        RegistrarOperacaoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);
}

public sealed class RegistrarOperacaoProdutoAcabado(
    IEstoqueProdutoAcabadoRepository repository) : IRegistrarOperacaoProdutoAcabado
{
    public Task<OperacaoProdutoAcabadoResult> ExecuteAsync(
        RegistrarOperacaoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return repository.RegistrarOperacaoAsync(input, cancellationToken);
    }
}
