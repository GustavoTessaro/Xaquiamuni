using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Tests;

public sealed class ConfirmarEntregaPedidoTests
{
    [Fact]
    public async Task RecusaIdentificadoresInvalidosOuAusenciaDeDeclaracaoAntesDaPersistencia()
    {
        var caso = new ConfirmarEntregaPedido(new Repositorio());
        var pedido = Guid.NewGuid();
        var administrador = Guid.NewGuid();
        Assert.Equal(ConfirmarEntregaPedidoStatus.DadosInvalidos, (await caso.ExecuteAsync(pedido, administrador, false)).Status);
        Assert.Equal(ConfirmarEntregaPedidoStatus.DadosInvalidos, (await caso.ExecuteAsync(Guid.Empty, administrador, true)).Status);
        Assert.Equal(ConfirmarEntregaPedidoStatus.DadosInvalidos, (await caso.ExecuteAsync(pedido, Guid.Empty, true)).Status);
    }

    private sealed class Repositorio : IConfirmarEntregaPedidoRepository
    {
        public Task<ConfirmarEntregaPedidoResult> ConfirmarEntregaAsync(
            Guid pedidoId, Guid administradorId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("O comando deveria ser recusado antes de acessar o repositório.");
    }
}
