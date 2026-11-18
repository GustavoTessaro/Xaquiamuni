using Xaquiamuni.Aplicacao.Estoque;

namespace Xaquiamuni.Tests;

public sealed class ReservarProdutoAcabadoTests
{
    [Fact]
    public async Task SolicitaAlocacaoComOsIdentificadoresEQuantidadeOriginais()
    {
        var repository = new RepositoryFake();
        var input = new ReservarProdutoAcabadoInput(Guid.NewGuid(), Guid.NewGuid(), 2);
        using var cancellation = new CancellationTokenSource();

        var result = await new ReservarProdutoAcabado(repository).ExecuteAsync(input, cancellation.Token);

        Assert.Same(input, repository.Input);
        Assert.Equal(cancellation.Token, repository.Token);
        Assert.Same(repository.Result, result);
    }

    [Fact]
    public async Task RejeitaDadosInvalidosAntesDeConsultarRepositorio()
    {
        var repository = new RepositoryFake();
        var casoDeUso = new ReservarProdutoAcabado(repository);
        var valid = new ReservarProdutoAcabadoInput(Guid.NewGuid(), Guid.NewGuid(), 2);

        foreach (var input in new[]
        {
            null, valid with { PedidoId = Guid.Empty }, valid with { ProdutoVarianteId = Guid.Empty },
            valid with { Quantidade = 0 }, valid with { Quantidade = -1 }
        })
        {
            var result = await casoDeUso.ExecuteAsync(input);
            Assert.Equal(ReservarProdutoAcabadoStatus.SolicitacaoInvalida, result.Status);
            Assert.Empty(result.Reservas);
        }

        Assert.Null(repository.Input);
    }

    private sealed class RepositoryFake : IReservaProdutoAcabadoRepository
    {
        public ReservarProdutoAcabadoInput? Input { get; private set; }
        public CancellationToken Token { get; private set; }
        public ReservarProdutoAcabadoResult Result { get; } =
            new(ReservarProdutoAcabadoStatus.SaldoInsuficiente, []);

        public Task<ReservarProdutoAcabadoResult> ReservarAsync(
            ReservarProdutoAcabadoInput input, CancellationToken cancellationToken = default)
        {
            Input = input;
            Token = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
