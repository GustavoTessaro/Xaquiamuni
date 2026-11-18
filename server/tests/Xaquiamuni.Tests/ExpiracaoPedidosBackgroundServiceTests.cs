using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Pedidos;

namespace Xaquiamuni.Tests;

public sealed class ExpiracaoPedidosBackgroundServiceTests
{
    [Fact]
    public void InfraestruturaAtivaSomenteQuandoSolicitado()
    {
        var semConfiguracao = new ServiceCollection();
        semConfiguracao.AddInfrastructureServices(new ConfigurationBuilder().Build());
        Assert.DoesNotContain(semConfiguracao, descriptor => descriptor.ImplementationType == typeof(ExpiracaoPedidosBackgroundService));

        var habilitado = new ServiceCollection();
        habilitado.AddInfrastructureServices(Configuracao("Enabled", "true"));
        Assert.Contains(habilitado, descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(ExpiracaoPedidosBackgroundService));
        using var provider = habilitado.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ExpiracaoPedidosWorkerOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(1), options.CycleInterval);
        Assert.Equal(50, options.BatchSize);
        Assert.Equal(4, options.MaxPagesPerCycle);
    }

    [Theory]
    [InlineData("CycleInterval", "00:00:00")]
    [InlineData("BatchSize", "101")]
    [InlineData("MaxPagesPerCycle", "0")]
    public void InfraestruturaRejeitaConfiguracaoInvalida(string chave, string valor)
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices(Configuracao(chave, valor));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<ExpiracaoPedidosWorkerOptions>>().Value);
    }

    [Fact]
    public async Task WorkerAvancaPeloCursorLimitaPaginasEReiniciaAposConcluirVarredura()
    {
        var inicio = DateTimeOffset.UnixEpoch;
        var primeiro = new CursorPedidosVencidos(inicio, Guid.NewGuid(), inicio.AddHours(1));
        var segundo = primeiro with { PedidoId = Guid.NewGuid() };
        var chamadas = new List<CursorPedidosVencidos?>();
        var contador = 0;
        var descartados = 0;
        await using var provider = CriarProvider((limite, cursor, _) =>
        {
            Assert.Equal(2, limite);
            chamadas.Add(cursor);
            contador++;
            return Task.FromResult(contador switch
            {
                1 => new LotePedidosVencidos([new(primeiro.PedidoId, EncerrarPedidoStatus.FalhaPersistencia)], primeiro),
                2 => new LotePedidosVencidos([new(segundo.PedidoId, EncerrarPedidoStatus.Expirado)], segundo),
                3 => new LotePedidosVencidos([new(Guid.NewGuid(), EncerrarPedidoStatus.Repetido)], null),
                _ => new LotePedidosVencidos([new(primeiro.PedidoId, EncerrarPedidoStatus.Expirado)], null),
            });
        }, () => descartados++);
        var tempo = new RelogioManual();
        var logger = new RegistroCiclosExpiracao();
        using var worker = CriarWorker(provider, tempo, logger, paginas: 2);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var espera = await tempo.ProximaEsperaAsync();
            Assert.Equal(TimeSpan.FromMinutes(1), espera.Intervalo);
            Assert.Equal(new CursorPedidosVencidos?[] { null, primeiro }, chamadas);
            Assert.Equal(1, descartados);
            var ciclo = await logger.ProximoAsync();
            Assert.Equal(2, ciclo["Processados"]);
            Assert.Equal(1, ciclo["Expirados"]);
            Assert.Equal(1, ciclo["Falhas"]);

            espera.Disparar();
            espera = await tempo.ProximaEsperaAsync();
            Assert.Equal(segundo, chamadas[2]);
            Assert.Equal(2, descartados);

            espera.Disparar();
            await tempo.ProximaEsperaAsync();
            Assert.Null(chamadas[3]);
            Assert.Equal(3, descartados);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(4, chamadas.Count);
    }

    [Fact]
    public async Task WorkerAguardaAntesDeRepetirFalhaSemRegistrarMensagemSensivel()
    {
        var chamadas = 0;
        var descartados = 0;
        await using var provider = CriarProvider((_, cursor, _) =>
        {
            Assert.Null(cursor);
            if (++chamadas == 1)
            {
                throw new TimeoutException("segredo do provedor");
            }

            return Task.FromResult(new LotePedidosVencidos([], null));
        }, () => descartados++);
        var tempo = new RelogioManual();
        var logger = new RegistroCiclosExpiracao();
        using var worker = CriarWorker(provider, tempo, logger);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var espera = await tempo.ProximaEsperaAsync();
            Assert.Equal(1, chamadas);
            Assert.Equal(1, descartados);
            Assert.True(Assert.IsType<bool>((await logger.ProximoAsync())["FalhaCiclo"]));
            var erro = Assert.Single(logger.Erros);
            Assert.Contains("TimeoutException", erro);
            Assert.DoesNotContain("segredo", erro);

            espera.Disparar();
            await tempo.ProximaEsperaAsync();
            Assert.Equal(2, chamadas);
            Assert.Equal(2, descartados);
            Assert.False(Assert.IsType<bool>((await logger.ProximoAsync())["FalhaCiclo"]));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task WorkerPropagaEncerramentoAoProcessamentoEmAndamento()
    {
        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var descartado = false;
        await using var provider = CriarProvider(async (_, _, cancellationToken) =>
        {
            iniciou.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new([], null);
            }
            finally
            {
                cancelou.TrySetResult();
            }
        }, () => descartado = true);
        using var worker = CriarWorker(provider, new RelogioManual(), new RegistroCiclosExpiracao());
        await worker.StartAsync(CancellationToken.None);
        await iniciou.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancelou.Task.IsCompletedSuccessfully);
        Assert.True(descartado);
    }

    private static IConfiguration Configuracao(string chave, string valor) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [$"ExpiracaoPedidosWorker:{chave}"] = valor })
        .Build();

    private static ServiceProvider CriarProvider(
        Func<int, CursorPedidosVencidos?, CancellationToken, Task<LotePedidosVencidos>> processar, Action descartar) =>
        new ServiceCollection().AddScoped<EncerrarPedidoCompra>()
            .AddScoped<IEncerrarPedidoCompraRepository>(_ => new Repositorio(processar, descartar))
            .BuildServiceProvider(validateScopes: true);

    private static ExpiracaoPedidosBackgroundService CriarWorker(
        ServiceProvider provider, TimeProvider tempo, RegistroCiclosExpiracao logger, int paginas = 1) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ExpiracaoPedidosWorkerOptions
        {
            Enabled = true, BatchSize = 2, MaxPagesPerCycle = paginas,
        }), tempo, logger);

    private sealed class Repositorio(
        Func<int, CursorPedidosVencidos?, CancellationToken, Task<LotePedidosVencidos>> processar,
        Action descartar) : IEncerrarPedidoCompraRepository, IAsyncDisposable
    {
        public Task<LotePedidosVencidos> ProcessarLoteAsync(int limite, CursorPedidosVencidos? apos, CancellationToken cancellationToken) =>
            processar(limite, apos, cancellationToken);

        public Task<EncerrarPedidoStatus> ExpirarAsync(Guid pedidoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EncerrarPedidoStatus> CancelarAsync(Guid pedidoId, Guid administradorId, string motivo,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            descartar();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RelogioManual : TimeProvider
    {
        private readonly Channel<EsperaManual> _esperas = Channel.CreateUnbounded<EsperaManual>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var espera = new EsperaManual(callback, state, dueTime);
            _esperas.Writer.TryWrite(espera);
            return espera;
        }

        public Task<EsperaManual> ProximaEsperaAsync() =>
            _esperas.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class EsperaManual(TimerCallback callback, object? state, TimeSpan intervalo) : ITimer
    {
        public TimeSpan Intervalo => intervalo;
        public void Disparar() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class RegistroCiclosExpiracao : ILogger<ExpiracaoPedidosBackgroundService>
{
    private readonly Channel<IReadOnlyDictionary<string, object?>> _ciclos =
        Channel.CreateUnbounded<IReadOnlyDictionary<string, object?>>();

    internal List<string> Erros { get; } = [];
    internal Task<IReadOnlyDictionary<string, object?>> ProximoAsync() =>
        _ciclos.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Error)
        {
            Erros.Add(formatter(state, exception));
        }

        if (state is IEnumerable<KeyValuePair<string, object?>> valores)
        {
            var campos = valores.ToDictionary();
            if (campos.ContainsKey("Processados"))
            {
                _ciclos.Writer.TryWrite(campos);
            }
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
