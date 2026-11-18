using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class EstoqueProdutoAcabadoAplicacaoTests
{
    [Fact]
    public async Task Consulta_UsesOperationalCalendarDateForAvailability()
    {
        var dataOperacional = new DateOnly(2026, 11, 17);
        var repository = new ConsultaRepositoryFake();
        var consulta = new ConsultarEstoqueProdutoAcabado(
            repository,
            new CalendarioFake(dataOperacional));

        await consulta.ConsultarPosicaoVarianteAsync(Guid.NewGuid());

        Assert.Equal(dataOperacional, repository.DataReferencia);
    }

    [Fact]
    public async Task Consulta_RejeitaFiltroDeHistoricoInvalidoAntesDaPersistencia()
    {
        var repository = new ConsultaRepositoryFake();
        var consulta = new ConsultarEstoqueProdutoAcabado(
            repository,
            new CalendarioFake(new DateOnly(2026, 11, 17)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            consulta.ListarMovimentosAsync(new ListarMovimentosProdutoAcabadoFiltro(
                Guid.NewGuid(),
                Tipo: (TipoMovimentoProdutoAcabado)99)));
        Assert.Null(repository.DataReferencia);
    }

    [Fact]
    public async Task Operacao_DelegaEntradaDeOperacaoAoRepositorio()
    {
        var repository = new OperacaoRepositoryFake();
        var useCase = new RegistrarOperacaoProdutoAcabado(repository);
        var input = new RegistrarOperacaoProdutoAcabadoInput(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            TipoMovimentoProdutoAcabado.Perda,
            2,
            CondicaoEstoqueProdutoAcabado.Danificada,
            null,
            "Quebra",
            "perda-1");

        await useCase.ExecuteAsync(input);

        Assert.Same(input, repository.Input);
    }

    private sealed class CalendarioFake(DateOnly data) : ICalendarioOperacionalEstoque
    {
        public DateOnly ObterDataAtual() => data;
        public DateOnly ObterDataEm(DateTimeOffset instanteUtc) => data;
    }

    private sealed class ConsultaRepositoryFake : IEstoqueProdutoAcabadoConsultaRepository
    {
        public DateOnly? DataReferencia { get; private set; }

        public Task<DetalheEstoqueProdutoAcabadoConsulta?> ConsultarPosicaoVarianteAsync(
            Guid produtoVarianteId,
            DateOnly dataReferencia,
            CancellationToken cancellationToken = default)
        {
            DataReferencia = dataReferencia;
            return Task.FromResult<DetalheEstoqueProdutoAcabadoConsulta?>(null);
        }

        public Task<PosicaoLoteProdutoAcabadoConsulta?> ConsultarPosicaoLoteAsync(
            Guid produtoVarianteId,
            Guid loteId,
            DateOnly dataReferencia,
            CancellationToken cancellationToken = default)
        {
            DataReferencia = dataReferencia;
            return Task.FromResult<PosicaoLoteProdutoAcabadoConsulta?>(null);
        }

        public Task<PaginaEstoque<MovimentoProdutoAcabadoConsulta>> ListarMovimentosAsync(
            ListarMovimentosProdutoAcabadoFiltro filtro,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaginaEstoque<MovimentoProdutoAcabadoConsulta>(
                [],
                0,
                filtro.Pagina,
                filtro.ItensPorPagina));
    }

    private sealed class OperacaoRepositoryFake : IEstoqueProdutoAcabadoRepository
    {
        public RegistrarOperacaoProdutoAcabadoInput? Input { get; private set; }

        public Task<RegistrarEntradaProdutoAcabadoResult> RegistrarEntradaAsync(
            RegistrarEntradaProdutoAcabadoInput input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperacaoProdutoAcabadoResult> RegistrarOperacaoAsync(
            RegistrarOperacaoProdutoAcabadoInput input,
            CancellationToken cancellationToken = default)
        {
            Input = input;
            return Task.FromResult(OperacaoProdutoAcabadoResult.Failure(
                OperacaoProdutoAcabadoStatus.OperacaoInvalida,
                "Teste"));
        }

        public Task<ContarProdutoAcabadoResult> ContarAsync(
            ContarProdutoAcabadoInput input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EstornarMovimentoProdutoAcabadoResult> EstornarAsync(
            EstornarMovimentoProdutoAcabadoInput input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConfigurarPoliticaProdutoAcabadoResult> ConfigurarPoliticaAsync(
            ConfigurarPoliticaProdutoAcabadoInput input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
