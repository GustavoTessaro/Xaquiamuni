using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record ReservarProdutoAcabadoInput(Guid PedidoId, Guid ProdutoVarianteId, int Quantidade);

public enum ReservarProdutoAcabadoStatus
{
    Sucesso,
    SolicitacaoInvalida,
    VarianteNaoEncontrada,
    VarianteInativa,
    SaldoInsuficiente,
    ConflitoIdempotencia,
    ConflitoConcorrencia
}

public sealed record ReservarProdutoAcabadoResult(
    ReservarProdutoAcabadoStatus Status,
    IReadOnlyList<ReservaProdutoAcabado> Reservas,
    bool Repetida = false)
{
    public bool Sucesso => Status == ReservarProdutoAcabadoStatus.Sucesso;
}

public interface IReservaProdutoAcabadoRepository
{
    Task<ReservarProdutoAcabadoResult> ReservarAsync(
        ReservarProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);
}

public interface IReservarProdutoAcabado
{
    Task<ReservarProdutoAcabadoResult> ExecuteAsync(
        ReservarProdutoAcabadoInput? input,
        CancellationToken cancellationToken = default);
}

public sealed class ReservarProdutoAcabado(
    IReservaProdutoAcabadoRepository repository) : IReservarProdutoAcabado
{
    public Task<ReservarProdutoAcabadoResult> ExecuteAsync(
        ReservarProdutoAcabadoInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null || input.PedidoId == Guid.Empty
            || input.ProdutoVarianteId == Guid.Empty || input.Quantidade <= 0)
        {
            return Task.FromResult(new ReservarProdutoAcabadoResult(
                ReservarProdutoAcabadoStatus.SolicitacaoInvalida, []));
        }

        return repository.ReservarAsync(input, cancellationToken);
    }
}
