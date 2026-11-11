using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Tests;

public class AuthRegistrationEndpointTests
{
    [Fact]
    public async Task PostRegister_WithoutAntiforgeryToken_IsRejectedAndNotCached()
    {
        var fake = new FakeCadastrarCliente(await CreateApplicationResultAsync(ValidInput()));
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(AuthEndpoints.RegisterEndpoint, ValidRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task PostRegister_WithInvalidAntiforgeryToken_IsRejectedAndNotCached()
    {
        var fake = new FakeCadastrarCliente(await CreateApplicationResultAsync(ValidInput()));
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);
        _ = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), "invalid-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task PostRegister_WithMalformedJson_IsRejectedAndNotCached()
    {
        var fake = new FakeCadastrarCliente(await CreateApplicationResultAsync(ValidInput()));
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.RegisterEndpoint)
        {
            Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task PostRegister_WithInvalidInput_ReturnsFieldErrorsWithoutSensitiveValues()
    {
        var invalidInput = ValidInput("not-a-cpf", "Senha privada de teste");
        var fake = new FakeCadastrarCliente(await CreateApplicationResultAsync(invalidInput));
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);

        using var request = CreateRequest(
            ValidRequest(invalidInput.Cpf!, invalidInput.Senha!),
            token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("\"Cpf\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidInput.Cpf!, body, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidInput.Senha!, body, StringComparison.Ordinal);
        Assert.Equal(1, fake.Calls);
    }

    [Theory]
    [InlineData(CadastroClienteStatus.EmailAlreadyRegistered)]
    [InlineData(CadastroClienteStatus.CpfAlreadyRegistered)]
    public async Task PostRegister_WhenEmailOrCpfIsDuplicate_ReturnsGenericConflict(
        CadastroClienteStatus status)
    {
        var fake = new FakeCadastrarCliente(
            await CreateApplicationResultAsync(ValidInput(), status));
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertNoStore(response);
        Assert.DoesNotContain("ana@example.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Senha forte", body, StringComparison.Ordinal);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task PostRegister_WhenIdentityRejectsPassword_ReturnsSafeValidationProblem()
    {
        const string rejectedPassword = "Rejected private password 123!";
        var invalidInput = ValidInput(senha: rejectedPassword);
        var fake = new FakeCadastrarCliente(
            await CreateApplicationResultAsync(
                invalidInput,
                CadastroClienteStatus.PasswordValidationFailed));
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(senha: rejectedPassword), token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        Assert.Contains("Senha", body, StringComparison.Ordinal);
        Assert.Contains("A senha não atende aos requisitos de segurança.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordTooShort", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rejectedPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@example.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", body, StringComparison.Ordinal);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task PostRegister_OnSuccessReturnsOnlyPublicIdWithoutSigningIn()
    {
        var result = await CreateApplicationResultAsync(ValidInput());
        var fake = new FakeCadastrarCliente(result);
        using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), token);
        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var properties = document.RootElement.EnumerateObject().ToArray();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal($"/api/clientes/{result.UserId}", response.Headers.Location?.OriginalString);
        Assert.Equal(["userId"], properties.Select(property => property.Name).ToArray());
        Assert.Equal(result.UserId, properties[0].Value.GetGuid());
        Assert.Equal(1, fake.Calls);
        Assert.Equal(ValidInput().NomeCompleto, fake.LastInput?.NomeCompleto);
        Assert.Equal(ValidInput().EnderecoInicial?.Cep, fake.LastInput?.EnderecoInicial?.Cep);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(
                "__Host-Xaquiamuni.Auth=",
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PostRegister_OnOperationalFailureReturnsGenericServerError()
    {
        var fake = new FakeCadastrarCliente(
            await CreateApplicationResultAsync(ValidInput(), CadastroClienteStatus.PersistenceFailed));
        using var loggerProvider = new CapturingLoggerProvider();
        using var factory = CreateFactory(fake, loggerProvider);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertNoStore(response);
        Assert.DoesNotContain("SQL", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("529.982.247-25", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Senha forte", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PERSISTENCE_TRANSACTION", body, StringComparison.Ordinal);
        var log = Assert.Single(
            loggerProvider.Entries,
            entry => entry.Message.Contains("Client registration could not be completed.", StringComparison.Ordinal));
        Assert.Contains("Category=PERSISTENCE_TRANSACTION", log.Message, StringComparison.Ordinal);
        Assert.Contains("Stage=TRANSACTION_SAVE", log.Message, StringComparison.Ordinal);
        Assert.Null(log.ExceptionType);
    }

    [Fact]
    public async Task PostRegister_OnIdentityFailureLogsOnlySafeCodes()
    {
        var fake = new FakeCadastrarCliente(
            await CreateApplicationResultAsync(ValidInput(), CadastroClienteStatus.IdentityCreationFailed));
        using var loggerProvider = new CapturingLoggerProvider();
        using var factory = CreateFactory(fake, loggerProvider);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("PasswordTooShort", body, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@example.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Senha forte", body, StringComparison.Ordinal);

        var log = Assert.Single(
            loggerProvider.Entries,
            entry => entry.Message.Contains("Client registration could not be completed.", StringComparison.Ordinal));
        Assert.Contains("Category=IDENTITY", log.Message, StringComparison.Ordinal);
        Assert.Contains("Stage=IDENTITY_USER_CREATE", log.Message, StringComparison.Ordinal);
        Assert.Contains("IdentityErrorCodes=UNKNOWN_IDENTITY_ERROR", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@example.com", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Senha forte", log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostRegister_OnUnexpectedExceptionReturnsGenericServerError()
    {
        var fake = new FakeCadastrarCliente(new InvalidOperationException(
            "Internal details with 529.982.247-25 and secret password"));
        using var loggerProvider = new CapturingLoggerProvider();
        using var factory = CreateFactory(fake, loggerProvider);
        using var client = CreateClient(factory);
        var token = await GetRequestToken(client);

        using var request = CreateRequest(ValidRequest(), token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertNoStore(response);
        Assert.DoesNotContain("Internal details", body, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret password", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CONFIGURATION_INFRASTRUCTURE", body, StringComparison.Ordinal);
        var log = Assert.Single(
            loggerProvider.Entries,
            entry => entry.Message.Contains("Client registration could not be completed.", StringComparison.Ordinal));
        Assert.Contains("Category=CONFIGURATION_INFRASTRUCTURE", log.Message, StringComparison.Ordinal);
        Assert.Contains("Stage=USE_CASE_ENTRY", log.Message, StringComparison.Ordinal);
        Assert.Contains("ExceptionType=INVALID_OPERATION", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal details", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret password", log.Message, StringComparison.Ordinal);
        Assert.Null(log.ExceptionType);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeCadastrarCliente fake,
        CapturingLoggerProvider? loggerProvider = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            if (loggerProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = string.Empty
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICadastrarCliente>();
                services.AddSingleton<ICadastrarCliente>(fake);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static HttpRequestMessage CreateRequest(RegisterClientRequest payload, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoints.RegisterEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        if (token is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, token);
        }

        return request;
    }

    private static async Task<string> GetRequestToken(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return Assert.IsType<string>(body?.RootElement.GetProperty("requestToken").GetString());
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    provider.Entries.Enqueue(new CapturedLog(
                        formatter(state, exception),
                        exception?.GetType().Name));
                }
            }
        }
    }

    private sealed record CapturedLog(string Message, string? ExceptionType);

    private static CadastroClienteInput ValidInput(
        string cpf = "529.982.247-25",
        string senha = "Senha forte sem regras extras") => new()
    {
        NomeCompleto = "Ana Maria Silva",
        Cpf = cpf,
        Email = "ana@example.com",
        Telefone = "+55 (11) 91234-5678",
        Senha = senha,
        EnderecoInicial = new EnderecoInicialInput
        {
            Cep = "95000-000",
            Uf = "RS",
            Cidade = "Vacaria",
            Bairro = "Centro",
            Logradouro = "Rua das Flores",
            Numero = "10",
            Complemento = "Casa 2"
        }
    };

    private static RegisterClientRequest ValidRequest(
        string cpf = "529.982.247-25",
        string senha = "Senha forte sem regras extras") => new()
    {
        NomeCompleto = "Ana Maria Silva",
        Cpf = cpf,
        Email = "ana@example.com",
        Telefone = "+55 (11) 91234-5678",
        Senha = senha,
        EnderecoInicial = new RegisterClientAddressRequest
        {
            Cep = "95000-000",
            Uf = "RS",
            Cidade = "Vacaria",
            Bairro = "Centro",
            Logradouro = "Rua das Flores",
            Numero = "10",
            Complemento = "Casa 2"
        }
    };

    private static async Task<CadastroClienteResult> CreateApplicationResultAsync(
        CadastroClienteInput input,
        CadastroClienteStatus? requestedStatus = null)
    {
        var identity = new FakeIdentityService();
        var transaction = new FakeTransaction();
        if (requestedStatus == CadastroClienteStatus.EmailAlreadyRegistered)
        {
            identity.CreateResult = ClienteIdentityOperationResult.DuplicateEmail();
        }
        else if (requestedStatus == CadastroClienteStatus.PersistenceFailed)
        {
            transaction.SaveException = new ClientePersistenceException();
        }
        else if (requestedStatus == CadastroClienteStatus.CpfAlreadyRegistered)
        {
            transaction.SaveException = new ClienteCpfDuplicadoException();
        }
        else if (requestedStatus == CadastroClienteStatus.IdentityCreationFailed)
        {
            identity.CreateResult = ClienteIdentityOperationResult.Failed(["UnknownCustomCode"]);
        }
        else if (requestedStatus == CadastroClienteStatus.PasswordValidationFailed)
        {
            identity.CreateResult =
                ClienteIdentityOperationResult.PasswordValidationFailed(["PasswordTooShort"]);
        }

        var useCase = new CadastrarCliente(
            new CadastroClienteInputValidator(),
            new FakeCpfProtectionService(),
            identity,
            new FakeClienteRepository(),
            new FakeTransactionFactory(transaction));
        return await useCase.ExecuteAsync(input);
    }

    private sealed class FakeCadastrarCliente : ICadastrarCliente
    {
        private readonly CadastroClienteResult? _result;
        private readonly Exception? _exception;

        public FakeCadastrarCliente(CadastroClienteResult result)
        {
            _result = result;
        }

        public FakeCadastrarCliente(Exception exception)
        {
            _exception = exception;
        }

        public int Calls { get; private set; }
        public CadastroClienteInput? LastInput { get; private set; }

        public Task<CadastroClienteResult> ExecuteAsync(
            CadastroClienteInput? input,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastInput = input;
            return _exception is null
                ? Task.FromResult(_result ?? throw new InvalidOperationException("Fake result missing"))
                : Task.FromException<CadastroClienteResult>(_exception);
        }
    }

    private sealed class FakeIdentityService : IClienteIdentityService
    {
        public ClienteIdentityOperationResult CreateResult { get; set; } =
            ClienteIdentityOperationResult.Success();

        public Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<ClienteIdentityOperationResult> CreateUserAsync(
            Guid userId,
            string email,
            string phoneNumber,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult);

        public Task<ClienteIdentityOperationResult> AddToRoleAsync(
            Guid userId,
            string role,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ClienteIdentityOperationResult.Success());
    }

    private sealed class FakeClienteRepository : IClienteRepository
    {
        public void Adicionar(ClientePerfil perfil) { }
        public void Adicionar(ClienteEndereco endereco) { }
    }

    private sealed class FakeTransactionFactory(FakeTransaction transaction) : IClienteTransactionFactory
    {
        public Task<IClienteTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IClienteTransaction>(transaction);
    }

    private sealed class FakeTransaction : IClienteTransaction
    {
        public Exception? SaveException { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            SaveException is null
                ? Task.FromResult(2)
                : Task.FromException<int>(SaveException);

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCpfProtectionService : ICpfProtectionService
    {
        public CpfProtegido Protect(Cpf cpf) => new(
            Convert.ToBase64String(new byte[11]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[16]),
            new string('A', 64),
            "enc-v1",
            "fp-v1");

        public Cpf RecoverForAuthorizedUse(CpfProtegido protectedCpf) => throw new NotSupportedException();
        public string CreateFingerprintForLookup(Cpf cpf) => new('A', 64);
    }
}
