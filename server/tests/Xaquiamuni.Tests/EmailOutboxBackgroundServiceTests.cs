using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Tests;

public sealed class EmailOutboxBackgroundServiceTests
{
    [Fact]
    public void AddInfrastructureServices_LeavesWorkerDisabledByDefault()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EmailOutboxBackgroundServiceOptions>>().Value;
        Assert.False(options.Enabled);
    }

    [Fact]
    public void AddInfrastructureServices_RejectsActivationWithoutEmailSender()
    {
        var configuration = CreateConfiguration(("Enabled", "true"));
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructureServices(configuration));

        Assert.Equal("The email outbox worker requires a configured email sender.", exception.Message);
    }

    [Theory]
    [InlineData("CycleInterval", "00:00:00")]
    [InlineData("CycleInterval", "00:10:00")]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "101")]
    public void Options_RejectIntervalAndBatchOutsideSupportedRanges(string key, string value)
    {
        var configuration = CreateConfiguration(("Enabled", "true"), (key, value));
        var services = new ServiceCollection();
        services.AddSingleton<IEmailSender, FakeEmailSender>();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EmailOutboxBackgroundServiceOptions>>();

        Assert.Throws<OptionsValidationException>(() => _ = options.Value);
    }

    [Fact]
    public async Task Worker_ClaimsSequentiallyAndStopsAtBatchLimit()
    {
        EmailOutboxClaim[] claims =
        [
            CreateClaim(),
            CreateClaim(),
            CreateClaim()
        ];
        var state = new WorkerState(claims, completionsBeforeSignal: 1);
        var composer = new FailFirstMessageComposer(state, failFirstMessage: true);
        using var provider = CreateWorkerProvider(state, composer);
        var worker = CreateWorker(provider, new EmailOutboxBackgroundServiceOptions
        {
            Enabled = true,
            CycleInterval = TimeSpan.FromMinutes(1),
            BatchSize = 2
        });

        await worker.StartAsync(CancellationToken.None);
        await state.BatchProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(stopTimeout.Token);

        Assert.Equal(1, state.ScopeCreatedCount);
        Assert.Equal(2, state.ClaimCalls);
        Assert.Equal(new[] { 1, 1 }, state.ClaimBatchSizes);
        Assert.All(
            state.ClaimMessageTypes,
            messageType => Assert.Equal(EmailOutboxMessageType.EmailConfirmation, messageType));
        Assert.Equal(new[] { claims[0].Id, claims[1].Id }, state.ReturnedClaimIds);
        Assert.Equal(
            ["claim", "token", "compose", "claim", "token", "compose", "send", "complete"],
            state.Operations);
        Assert.Equal(2, state.TokenGenerationCalls);
        Assert.Equal(2, composer.ComposeCalls);
        Assert.Equal(1, state.SendCalls);
        Assert.Equal(1, state.CompleteCalls);
    }

    [Fact]
    public async Task Worker_DoesNotReclaimMessageRetriedDuringSameCycle()
    {
        var retriedClaim = CreateClaim();
        var nextClaim = CreateClaim();
        var state = new WorkerState(
            [retriedClaim, retriedClaim, nextClaim],
            completionsBeforeSignal: 1,
            failFirstTokenGeneration: true);
        var composer = new FailFirstMessageComposer(state);
        using var provider = CreateWorkerProvider(state, composer);
        var worker = CreateWorker(provider, new EmailOutboxBackgroundServiceOptions
        {
            Enabled = true,
            CycleInterval = TimeSpan.FromMinutes(1),
            BatchSize = 3
        });

        await worker.StartAsync(CancellationToken.None);
        await state.NoMoreClaims.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(stopTimeout.Token);

        Assert.Equal(3, state.ClaimCalls);
        Assert.All(state.ClaimBatchSizes, batchSize => Assert.Equal(1, batchSize));
        Assert.All(
            state.ClaimMessageTypes,
            messageType => Assert.Equal(EmailOutboxMessageType.EmailConfirmation, messageType));
        Assert.Equal(new[] { retriedClaim.Id, nextClaim.Id }, state.ReturnedClaimIds);
        Assert.Equal(1, state.RetryCalls);
        Assert.Equal(2, state.TokenGenerationCalls);
        Assert.Equal(1, state.SendCalls);
        Assert.Equal(
            ["claim", "token-failed", "retry", "claim", "token", "compose", "send", "complete", "claim"],
            state.Operations);
    }

    [Fact]
    public async Task Worker_StopsCurrentCycleGracefullyWhenCancelled()
    {
        var state = new WorkerState([], completionsBeforeSignal: 1)
        {
            BlockClaimUntilCancelled = true
        };
        using var provider = CreateWorkerProvider(state, new FailFirstMessageComposer(state));
        var worker = CreateWorker(provider, new EmailOutboxBackgroundServiceOptions
        {
            Enabled = true,
            CycleInterval = TimeSpan.FromMinutes(1),
            BatchSize = 5
        });

        await worker.StartAsync(CancellationToken.None);
        await state.ClaimStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(stopTimeout.Token);

        Assert.Equal(1, state.ScopeCreatedCount);
        Assert.Equal(1, state.ClaimCalls);
        Assert.Equal(0, state.SendCalls);
    }

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(
                item => $"{EmailOutboxBackgroundServiceOptions.SectionName}:{item.Key}",
                item => (string?)item.Value))
            .Build();

    private static ServiceProvider CreateWorkerProvider(
        WorkerState state,
        FailFirstMessageComposer composer)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddScoped<IEmailOutboxStore>(serviceProvider =>
            new FakeOutboxStore(serviceProvider.GetRequiredService<WorkerState>()));
        services.AddSingleton<IEmailConfirmationService, FakeEmailConfirmationService>();
        services.AddSingleton<IEmailConfirmationMessageComposer>(composer);
        services.AddSingleton<IEmailSender, FakeEmailSender>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<EmailConfirmationOutboxProcessor>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static EmailOutboxBackgroundService CreateWorker(
        IServiceProvider provider,
        EmailOutboxBackgroundServiceOptions options) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<EmailOutboxBackgroundService>.Instance);

    private static EmailOutboxClaim CreateClaim() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailOutboxMessageType.EmailConfirmation,
            attempts: 1,
            DateTimeOffset.UtcNow.AddMinutes(5),
            [1, 2, 3, 4, 5, 6, 7, 8]);

    private sealed class WorkerState(
        IReadOnlyList<EmailOutboxClaim> claims,
        int completionsBeforeSignal,
        bool failFirstTokenGeneration = false)
    {
        public IReadOnlyList<EmailOutboxClaim> Claims { get; } = claims;
        public int CompletionsBeforeSignal { get; } = completionsBeforeSignal;
        public bool FailFirstTokenGeneration { get; } = failFirstTokenGeneration;
        public List<int> ClaimBatchSizes { get; } = [];
        public List<EmailOutboxMessageType> ClaimMessageTypes { get; } = [];
        public List<Guid> ReturnedClaimIds { get; } = [];
        public List<string> Operations { get; } = [];
        public TaskCompletionSource<bool> BatchProcessed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ClaimStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> NoMoreClaims { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockClaimUntilCancelled { get; init; }
        public int ScopeCreatedCount;
        public int ClaimCalls;
        public int TokenGenerationCalls;
        public int SendCalls;
        public int CompleteCalls;
        public int RetryCalls;
    }

    private sealed class FakeOutboxStore : IEmailOutboxStore
    {
        private readonly WorkerState _state;

        public FakeOutboxStore(WorkerState state)
        {
            _state = state;
            Interlocked.Increment(ref state.ScopeCreatedCount);
        }

        public async Task<IReadOnlyList<EmailOutboxClaim>> ClaimDueAsync(
            EmailOutboxMessageType messageType,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedMessageIds = null)
        {
            Interlocked.Increment(ref _state.ClaimCalls);
            _state.ClaimBatchSizes.Add(batchSize);
            _state.ClaimMessageTypes.Add(messageType);
            _state.Operations.Add("claim");
            _state.ClaimStarted.TrySetResult(true);
            if (_state.BlockClaimUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var excluded = excludedMessageIds?.ToHashSet() ?? [];
            var claim = _state.Claims.FirstOrDefault(candidate => !excluded.Contains(candidate.Id));
            if (claim is null)
            {
                _state.NoMoreClaims.TrySetResult(true);
                return [];
            }

            _state.ReturnedClaimIds.Add(claim.Id);
            return [claim];
        }

        public Task<bool> CompleteAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default)
        {
            _state.Operations.Add("complete");
            if (Interlocked.Increment(ref _state.CompleteCalls) >= _state.CompletionsBeforeSignal)
            {
                _state.BatchProcessed.TrySetResult(true);
            }

            return Task.FromResult(true);
        }

        public Task<bool> CancelAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> RetryAsync(
            EmailOutboxClaim claim,
            double jitterSample,
            CancellationToken cancellationToken = default)
        {
            _state.Operations.Add("retry");
            Interlocked.Increment(ref _state.RetryCalls);
            return Task.FromResult(true);
        }

        public Task<bool> DeadLetterAsync(
            EmailOutboxClaim claim,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

    }

    private sealed class FakeEmailConfirmationService(WorkerState state) : IEmailConfirmationService
    {
        public Task<Guid?> FindUnconfirmedUserIdByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref state.TokenGenerationCalls);
            if (state.FailFirstTokenGeneration && call == 1)
            {
                state.Operations.Add("token-failed");
                throw new InvalidOperationException();
            }

            state.Operations.Add("token");
            return Task.FromResult(
                EmailConfirmationTokenResult.GeneratedForUser(
                    $"client-{userId:N}@example.invalid",
                    "test-token"));
        }

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationResult> ConfirmEmailAsync(
            string email,
            string token,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailFirstMessageComposer(WorkerState state, bool failFirstMessage = false)
        : IEmailConfirmationMessageComposer
    {
        public int ComposeCalls { get; private set; }

        public EmailMessage Compose(EmailConfirmationTokenResult tokenResult)
        {
            ComposeCalls++;
            state.Operations.Add("compose");
            if (failFirstMessage && ComposeCalls == 1)
            {
                throw new NotSupportedException();
            }

            return new EmailMessage(
                "client@example.invalid",
                "Confirmation",
                "Confirmation body");
        }
    }

    private sealed class FakeEmailSender(WorkerState? state = null) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (state is not null)
            {
                Interlocked.Increment(ref state.SendCalls);
                state.Operations.Add("send");
            }

            return Task.CompletedTask;
        }
    }
}
