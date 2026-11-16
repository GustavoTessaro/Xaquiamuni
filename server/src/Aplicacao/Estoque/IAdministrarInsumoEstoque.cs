using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public interface IAdministrarInsumoEstoque
{
    Task<AdministrarInsumoEstoqueResult> CadastrarAsync(
        CadastrarInsumoEstoqueInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AtualizarAsync(
        AtualizarInsumoEstoqueInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AlterarStatusAsync(
        Guid insumoId,
        bool ativo,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> CadastrarFormaCompraAsync(
        CadastrarFormaCompraInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AtualizarFormaCompraAsync(
        AtualizarFormaCompraInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AlterarStatusFormaCompraAsync(
        Guid insumoId,
        Guid formaCompraId,
        bool ativa,
        CancellationToken cancellationToken = default);
}

public sealed record CadastrarInsumoEstoqueInput(
    string? Nome,
    TipoInsumoEstoque Tipo,
    UnidadeFisicaEstoque UnidadeBase,
    decimal? EstoqueMinimo,
    bool ControlaLote,
    bool ValidadeObrigatoria);

public sealed record AtualizarInsumoEstoqueInput(
    Guid InsumoId,
    string? Nome,
    TipoInsumoEstoque Tipo,
    decimal? EstoqueMinimo,
    bool ControlaLote,
    bool ValidadeObrigatoria);

public sealed record CadastrarFormaCompraInsumoInput(
    Guid InsumoId,
    string? Nome,
    decimal QuantidadeEquivalentePorEmbalagem,
    UnidadeFisicaEstoque Unidade);

public sealed record AtualizarFormaCompraInsumoInput(
    Guid InsumoId,
    Guid FormaCompraId,
    string? Nome,
    decimal QuantidadeEquivalentePorEmbalagem,
    UnidadeFisicaEstoque Unidade);

public enum AdministrarInsumoEstoqueStatus
{
    Sucesso,
    DadosInvalidos,
    InsumoNaoEncontrado,
    FormaCompraNaoEncontrada,
    HistoricoImpedeAlteracao,
    Conflito
}

public sealed record AdministrarInsumoEstoqueResult(
    AdministrarInsumoEstoqueStatus Status,
    InsumoEstoque? Insumo = null,
    FormaCompraInsumo? FormaCompra = null,
    string? Erro = null)
{
    public bool Sucesso => Status == AdministrarInsumoEstoqueStatus.Sucesso;

    public static AdministrarInsumoEstoqueResult Success(
        InsumoEstoque? insumo = null,
        FormaCompraInsumo? formaCompra = null) =>
        new(AdministrarInsumoEstoqueStatus.Sucesso, insumo, formaCompra);

    public static AdministrarInsumoEstoqueResult Failure(
        AdministrarInsumoEstoqueStatus status,
        string erro) =>
        new(status, null, null, erro);
}

public sealed class AdministrarInsumoEstoque(IEstoqueInsumoRepository repository)
    : IAdministrarInsumoEstoque
{
    public Task<AdministrarInsumoEstoqueResult> CadastrarAsync(
        CadastrarInsumoEstoqueInput input,
        CancellationToken cancellationToken = default) =>
        repository.CadastrarInsumoAsync(input, cancellationToken);

    public Task<AdministrarInsumoEstoqueResult> AtualizarAsync(
        AtualizarInsumoEstoqueInput input,
        CancellationToken cancellationToken = default) =>
        repository.AtualizarInsumoAsync(input, cancellationToken);

    public Task<AdministrarInsumoEstoqueResult> AlterarStatusAsync(
        Guid insumoId,
        bool ativo,
        CancellationToken cancellationToken = default) =>
        repository.AlterarStatusInsumoAsync(insumoId, ativo, cancellationToken);

    public Task<AdministrarInsumoEstoqueResult> CadastrarFormaCompraAsync(
        CadastrarFormaCompraInsumoInput input,
        CancellationToken cancellationToken = default) =>
        repository.CadastrarFormaCompraAsync(input, cancellationToken);

    public Task<AdministrarInsumoEstoqueResult> AtualizarFormaCompraAsync(
        AtualizarFormaCompraInsumoInput input,
        CancellationToken cancellationToken = default) =>
        repository.AtualizarFormaCompraAsync(input, cancellationToken);

    public Task<AdministrarInsumoEstoqueResult> AlterarStatusFormaCompraAsync(
        Guid insumoId,
        Guid formaCompraId,
        bool ativa,
        CancellationToken cancellationToken = default) =>
        repository.AlterarStatusFormaCompraAsync(
            insumoId,
            formaCompraId,
            ativa,
            cancellationToken);
}
