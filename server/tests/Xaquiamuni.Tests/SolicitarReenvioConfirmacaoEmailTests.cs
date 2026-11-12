using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Tests;

public sealed class SolicitarReenvioConfirmacaoEmailTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-email")]
    [InlineData("client@example")]
    public async Task ExecuteAsync_InvalidEmailDoesNotQueryIdentityOrOutbox(string? email)
    {
        var identity = new FakeEmailConfirmationService(Guid.NewGuid());
        var outbox = new FakeEmailConfirmationResendOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync(email);

        Assert.Equal(
            SolicitarReenvioConfirmacaoEmailStatus.InvalidEmail,
            result.Status);
        Assert.Equal(0, identity.LookupCalls);
        Assert.Equal(0, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_EmailAboveRegistrationLimitDoesNotQueryIdentityOrOutbox()
    {
        var identity = new FakeEmailConfirmationService(Guid.NewGuid());
        var outbox = new FakeEmailConfirmationResendOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync($"{new string('a', 245)}@example.com");

        Assert.Equal(
            SolicitarReenvioConfirmacaoEmailStatus.InvalidEmail,
            result.Status);
        Assert.Equal(0, identity.LookupCalls);
        Assert.Equal(0, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_NoEligibleAccountReturnsGenericResultWithoutCallingOutbox()
    {
        var identity = new FakeEmailConfirmationService(userId: null);
        var outbox = new FakeEmailConfirmationResendOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync("client@example.com");

        Assert.Equal(
            SolicitarReenvioConfirmacaoEmailStatus.RequestProcessed,
            result.Status);
        Assert.Equal(1, identity.LookupCalls);
        Assert.Equal(0, outbox.RequestCalls);
    }

    [Theory]
    [InlineData(EmailConfirmationResendOutboxResult.Enqueued)]
    [InlineData(EmailConfirmationResendOutboxResult.NotEligible)]
    [InlineData(EmailConfirmationResendOutboxResult.AlreadyQueued)]
    [InlineData(EmailConfirmationResendOutboxResult.CooldownActive)]
    [InlineData(EmailConfirmationResendOutboxResult.HourlyLimitReached)]
    public async Task ExecuteAsync_AllNormalOutboxResultsReturnSameGenericResult(
        EmailConfirmationResendOutboxResult outboxResult)
    {
        var userId = Guid.NewGuid();
        var identity = new FakeEmailConfirmationService(userId);
        var outbox = new FakeEmailConfirmationResendOutbox(outboxResult);
        var useCase = CreateUseCase(identity, outbox);

        var result = await useCase.ExecuteAsync("  client@example.com  ");

        Assert.Equal(
            SolicitarReenvioConfirmacaoEmailStatus.RequestProcessed,
            result.Status);
        Assert.Equal("client@example.com", identity.LastEmail);
        Assert.Equal(userId, outbox.LastUserId);
        Assert.Equal(1, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToIdentityAndOutbox()
    {
        using var cancellationSource = new CancellationTokenSource();
        var userId = Guid.NewGuid();
        var identity = new FakeEmailConfirmationService(userId);
        var outbox = new FakeEmailConfirmationResendOutbox();
        var useCase = CreateUseCase(identity, outbox);

        await useCase.ExecuteAsync("client@example.com", cancellationSource.Token);

        Assert.Equal(cancellationSource.Token, identity.LastCancellationToken);
        Assert.Equal(cancellationSource.Token, outbox.LastCancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesUnexpectedIdentityFailure()
    {
        var expectedException = new InvalidOperationException("Identity failure.");
        var identity = new FakeEmailConfirmationService(
            userId: null,
            lookupException: expectedException);
        var outbox = new FakeEmailConfirmationResendOutbox();
        var useCase = CreateUseCase(identity, outbox);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync("client@example.com"));

        Assert.Same(expectedException, exception);
        Assert.Equal(0, outbox.RequestCalls);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesUnexpectedOutboxFailure()
    {
        var expectedException = new InvalidOperationException("Persistence failure.");
        var identity = new FakeEmailConfirmationService(Guid.NewGuid());
        var outbox = new FakeEmailConfirmationResendOutbox(exception: expectedException);
        var useCase = CreateUseCase(identity, outbox);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync("client@example.com"));

        Assert.Same(expectedException, exception);
    }

    private static SolicitarReenvioConfirmacaoEmail CreateUseCase(
        FakeEmailConfirmationService identity,
        FakeEmailConfirmationResendOutbox outbox) =>
        new(new CadastroClienteInputValidator(), identity, outbox);

    private sealed class FakeEmailConfirmationService(
        Guid? userId,
        Exception? lookupException = null) : IEmailConfirmationService
    {
        public int LookupCalls { get; private set; }
        public string? LastEmail { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Guid?> FindUnconfirmedUserIdByEmailAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            LastEmail = email;
            LastCancellationToken = cancellationToken;
            return lookupException is null
                ? Task.FromResult(userId)
                : Task.FromException<Guid?>(lookupException);
        }

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationTokenResult> GenerateEmailConfirmationTokenForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailConfirmationResult> ConfirmEmailAsync(
            string email,
            string token,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeEmailConfirmationResendOutbox(
        EmailConfirmationResendOutboxResult result = EmailConfirmationResendOutboxResult.Enqueued,
        Exception? exception = null) : IEmailConfirmationResendOutbox
    {
        public int RequestCalls { get; private set; }
        public Guid LastUserId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<EmailConfirmationResendOutboxResult> RequestAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            RequestCalls++;
            LastUserId = userId;
            LastCancellationToken = cancellationToken;
            return exception is null
                ? Task.FromResult(result)
                : Task.FromException<EmailConfirmationResendOutboxResult>(exception);
        }
    }
}
