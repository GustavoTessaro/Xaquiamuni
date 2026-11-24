using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record ConfigurarPoliticaProdutoAcabadoInput(
    Guid ProdutoVarianteId,
    int? EstoqueMinimo,
    int? PrazoValidadePadraoDias,
    string? RedefinirSeCorresponder = null)
{
    public static string Precondicao(int? minimo, int? validade) =>
        FormattableString.Invariant($"politica:{minimo}:{validade}");
}

public enum ConfigurarPoliticaProdutoAcabadoStatus : byte
{
    Sucesso = 1,
    DadosInvalidos = 2,
    VarianteNaoEncontrada = 3,
    Conflito = 4
}

public sealed record ConfigurarPoliticaProdutoAcabadoResult(
    ConfigurarPoliticaProdutoAcabadoStatus Status,
    PoliticaEstoqueProdutoVariante? Politica,
    string? Erro)
{
    public bool Sucesso => Status == ConfigurarPoliticaProdutoAcabadoStatus.Sucesso;
}

public interface IConfigurarPoliticaEstoqueProdutoAcabado
{
    Task<ConfigurarPoliticaProdutoAcabadoResult> ExecuteAsync(
        ConfigurarPoliticaProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigurarPoliticaEstoqueProdutoAcabado(
    IEstoqueProdutoAcabadoRepository repository) : IConfigurarPoliticaEstoqueProdutoAcabado
{
    public Task<ConfigurarPoliticaProdutoAcabadoResult> ExecuteAsync(
        ConfigurarPoliticaProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return repository.ConfigurarPoliticaAsync(input, cancellationToken);
    }
}
