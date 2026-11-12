using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Outbox;

namespace Xaquiamuni.Tests;

public sealed class SolicitarRecuperacaoSenhaTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-email")]
    [InlineData("client@example")]
    public async Task ExecuteAsync_InvalidEmailReturnsInvalidInputWithoutCallingDependencies(string? email)
    {
        var identity = new FakePasswordResetService(Guid.NewGuid());
        var outbox = new FakePasswordResetOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync(email);

        Assert.Equal(SolicitarRecuperacaoSenhaStatus.InvalidEmail, result.Status);
        Assert.Equal(0, identity.LookupCalls);
        Assert.Equal(0, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_EmailAboveRegistrationLimitReturnsInvalidInput()
    {
        var identity = new FakePasswordResetService(Guid.NewGuid());
        var outbox = new FakePasswordResetOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync($"{new string('a', 245)}@example.com");

        Assert.Equal(SolicitarRecuperacaoSenhaStatus.InvalidEmail, result.Status);
        Assert.Equal(0, identity.LookupCalls);
        Assert.Equal(0, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_ValidEmailWithOuterSpacesIsTrimmedBeforeIdentityLookup()
    {
        var userId = Guid.NewGuid();
        var identity = new FakePasswordResetService(userId);
        var outbox = new FakePasswordResetOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync("  client@example.com  ");

        Assert.Equal(SolicitarRecuperacaoSenhaStatus.RequestProcessed, result.Status);
        Assert.Equal("client@example.com", identity.LastEmail);
        Assert.Equal(userId, outbox.LastUserId);
        Assert.Equal(1, identity.LookupCalls);
        Assert.Equal(1, outbox.RequestCalls);
        Assert.Equal(0, identity.TokenGenerationCalls);
        Assert.Equal(0, identity.ResetPasswordCalls);
    }

    [Fact]
    public async Task ExecuteAsync_NonexistentOrUnconfirmedAccountReturnsGenericResultWithoutOutboxCall()
    {
        var identity = new FakePasswordResetService(userId: null);
        var outbox = new FakePasswordResetOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync("client@example.com");

        Assert.Equal(SolicitarRecuperacaoSenhaStatus.RequestProcessed, result.Status);
        Assert.Equal("[RequestProcessed]", result.ToString());
        Assert.Equal(1, identity.LookupCalls);
        Assert.Equal(0, outbox.RequestCalls);
    }

    [Theory]
    [InlineData(PasswordResetOutboxResult.Enqueued)]
    [InlineData(PasswordResetOutboxResult.NotEligible)]
    [InlineData(PasswordResetOutboxResult.AlreadyQueued)]
    [InlineData(PasswordResetOutboxResult.CooldownActive)]
    [InlineData(PasswordResetOutboxResult.HourlyLimitReached)]
    public async Task ExecuteAsync_AllNormalOutboxResultsReturnSameGenericResult(
        PasswordResetOutboxResult outboxResult)
    {
        var userId = Guid.NewGuid();
        var identity = new FakePasswordResetService(userId);
        var outbox = new FakePasswordResetOutbox(outboxResult);
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync("client@example.com");

        Assert.Equal(SolicitarRecuperacaoSenhaStatus.RequestProcessed, result.Status);
        Assert.Equal(1, identity.LookupCalls);
        Assert.Equal(userId, outbox.LastUserId);
        Assert.Equal(1, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToIdentityAndOutbox()
    {
        using var cancellationSource = new CancellationTokenSource();
        var identity = new FakePasswordResetService(Guid.NewGuid());
        var outbox = new FakePasswordResetOutbox();
        var useCase = CreateUseCase(identity, outbox);

        await useCase.ExecuteAsync("client@example.com", cancellationSource.Token);

        Assert.Equal(cancellationSource.Token, identity.LastCancellationToken);
        Assert.Equal(cancellationSource.Token, outbox.LastCancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenCancelledDuringIdentityLookup()
    {
        using var cancellationSource = new CancellationTokenSource();
        var identity = new FakePasswordResetService((_, _) =>
        {
            cancellationSource.Cancel();
            return Task.FromResult<Guid?>(Guid.NewGuid());
        });
        var outbox = new FakePasswordResetOutbox();
        var useCase = CreateUseCase(identity, outbox);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync("client@example.com", cancellationSource.Token));

        Assert.Equal(0, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenCancelledDuringOutboxRequest()
    {
        using var cancellationSource = new CancellationTokenSource();
        var identity = new FakePasswordResetService(Guid.NewGuid());
        var outbox = new FakePasswordResetOutbox((_, _) =>
        {
            cancellationSource.Cancel();
            return Task.FromResult(PasswordResetOutboxResult.Enqueued);
        });
        var useCase = CreateUseCase(identity, outbox);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync("client@example.com", cancellationSource.Token));
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesUnexpectedIdentityAndOutboxFailures()
    {
        var identityFailure = new InvalidOperationException("Identity failure.");
        var identityUseCase = CreateUseCase(
            new FakePasswordResetService((_, _) => Task.FromException<Guid?>(identityFailure)),
            new FakePasswordResetOutbox());

        var identityException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => identityUseCase.ExecuteAsync("client@example.com"));
        Assert.Same(identityFailure, identityException);

        var outboxFailure = new InvalidOperationException("Outbox failure.");
        var outboxUseCase = CreateUseCase(
            new FakePasswordResetService(Guid.NewGuid()),
            new FakePasswordResetOutbox((_, _) => Task.FromException<PasswordResetOutboxResult>(outboxFailure)));

        var outboxException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => outboxUseCase.ExecuteAsync("client@example.com"));
        Assert.Same(outboxFailure, outboxException);
    }

    [Fact]
    public void DependencyInjection_ResolvesScopedUseCaseWithWorkerAndResendDisabled()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddInfrastructureServices(new ConfigurationBuilder().Build());

        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ISolicitarRecuperacaoSenha));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IEmailSender));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service is EmailOutboxBackgroundService);
        using var scope = provider.CreateScope();
        Assert.IsType<SolicitarRecuperacaoSenha>(
            scope.ServiceProvider.GetRequiredService<ISolicitarRecuperacaoSenha>());
    }

    private static SolicitarRecuperacaoSenha CreateUseCase(
        FakePasswordResetService identity,
        FakePasswordResetOutbox outbox) =>
        new(new CadastroClienteInputValidator(), identity, outbox);

    private sealed class FakePasswordResetService : IPasswordResetService
    {
        private readonly Func<string?, CancellationToken, Task<Guid?>> _lookup;

        public FakePasswordResetService(Guid? userId)
            : this((_, _) => Task.FromResult(userId))
        {
        }

        public FakePasswordResetService(
            Func<string?, CancellationToken, Task<Guid?>> lookup)
        {
            _lookup = lookup;
        }

        public int LookupCalls { get; private set; }
        public int TokenGenerationCalls { get; private set; }
        public int ResetPasswordCalls { get; private set; }
        public string? LastEmail { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Guid?> FindConfirmedUserIdByEmailAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            LastEmail = email;
            LastCancellationToken = cancellationToken;
            return _lookup(email, cancellationToken);
        }

        public Task<PasswordResetTokenResult> GeneratePasswordResetTokenForUserAsync(
            Guid userId,
            ReadOnlyMemory<byte>? credentialGenerationFingerprint,
            CancellationToken cancellationToken = default)
        {
            TokenGenerationCalls++;
            throw new NotSupportedException();
        }

        public Task<PasswordResetResult> ResetPasswordAsync(
            string? email,
            string? token,
            string? newPassword,
            CancellationToken cancellationToken = default)
        {
            ResetPasswordCalls++;
            throw new NotSupportedException();
        }
    }

    private sealed class FakePasswordResetOutbox : IPasswordResetOutbox
    {
        private readonly Func<Guid, CancellationToken, Task<PasswordResetOutboxResult>> _request;

        public FakePasswordResetOutbox(
            PasswordResetOutboxResult result = PasswordResetOutboxResult.Enqueued)
            : this((_, _) => Task.FromResult(result))
        {
        }

        public FakePasswordResetOutbox(
            Func<Guid, CancellationToken, Task<PasswordResetOutboxResult>> request)
        {
            _request = request;
        }

        public int RequestCalls { get; private set; }
        public Guid LastUserId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<PasswordResetOutboxResult> RequestAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            RequestCalls++;
            LastUserId = userId;
            LastCancellationToken = cancellationToken;
            return _request(userId, cancellationToken);
        }
    }
}
