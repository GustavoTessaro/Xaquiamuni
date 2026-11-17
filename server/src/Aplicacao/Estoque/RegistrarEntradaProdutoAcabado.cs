using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record RegistrarEntradaProdutoAcabadoInput(
    Guid ProdutoVarianteId,
    Guid ResponsavelId,
    TipoMovimentoProdutoAcabado Tipo,
    int Quantidade,
    CondicaoEstoqueProdutoAcabado CondicaoDestino,
    string? Motivo,
    string? ChaveIdempotencia,
    Guid? LoteId = null,
    DateOnly? DataFabricacaoNovoLote = null,
    DateOnly? DataValidadeNovoLote = null,
    string? Observacao = null);

public enum RegistrarEntradaProdutoAcabadoStatus : byte
{
    Sucesso = 1,
    EntradaInvalida = 2,
    VarianteNaoEncontrada = 3,
    LoteNaoEncontrado = 4,
    LoteDeOutraVariante = 5,
    EstoqueInicialJaRegistrado = 6,
    ConflitoIdempotencia = 7,
    ConflitoConcorrencia = 8
}

public sealed record RegistrarEntradaProdutoAcabadoResult(
    RegistrarEntradaProdutoAcabadoStatus Status,
    string? Erro,
    MovimentoProdutoAcabado? Movimento,
    LoteProdutoAcabado? Lote,
    bool Repetida)
{
    public bool Sucesso => Status == RegistrarEntradaProdutoAcabadoStatus.Sucesso;

    public static RegistrarEntradaProdutoAcabadoResult Success(
        MovimentoProdutoAcabado movimento,
        LoteProdutoAcabado lote,
        bool repetida = false) =>
        new(RegistrarEntradaProdutoAcabadoStatus.Sucesso, null, movimento, lote, repetida);

    public static RegistrarEntradaProdutoAcabadoResult Failure(
        RegistrarEntradaProdutoAcabadoStatus status,
        string erro) =>
        new(status, erro, null, null, false);
}

public interface IEstoqueProdutoAcabadoRepository
{
    Task<RegistrarEntradaProdutoAcabadoResult> RegistrarEntradaAsync(
        RegistrarEntradaProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);

    Task<OperacaoProdutoAcabadoResult> RegistrarOperacaoAsync(
        RegistrarOperacaoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);

    Task<ContarProdutoAcabadoResult> ContarAsync(
        ContarProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);

    Task<EstornarMovimentoProdutoAcabadoResult> EstornarAsync(
        EstornarMovimentoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);

    Task<ConfigurarPoliticaProdutoAcabadoResult> ConfigurarPoliticaAsync(
        ConfigurarPoliticaProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);
}

public interface IRegistrarEntradaProdutoAcabado
{
    Task<RegistrarEntradaProdutoAcabadoResult> ExecuteAsync(
        RegistrarEntradaProdutoAcabadoInput? input,
        CancellationToken cancellationToken = default);
}

public sealed class RegistrarEntradaProdutoAcabado(
    IEstoqueProdutoAcabadoRepository repository) : IRegistrarEntradaProdutoAcabado
{
    public Task<RegistrarEntradaProdutoAcabadoResult> ExecuteAsync(
        RegistrarEntradaProdutoAcabadoInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Task.FromResult(RegistrarEntradaProdutoAcabadoResult.Failure(
                RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida,
                "Os dados da entrada são obrigatórios."));
        }

        return repository.RegistrarEntradaAsync(input, cancellationToken);
    }
}
