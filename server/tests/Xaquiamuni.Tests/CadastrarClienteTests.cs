using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Aplicacao.Seguranca;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Tests;

public class CadastrarClienteTests
{
    [Fact]
    public async Task ExecuteAsync_ComDadosValidosCriaClienteEConfirmaAposPersistencia()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(ValidInput());

        Assert.True(result.Succeeded);
        Assert.Equal(CadastroClienteStatus.Success, result.Status);
        Assert.NotEqual(Guid.Empty, result.UserId);
        Assert.NotNull(fixture.Repository.Profile);
        Assert.NotNull(fixture.Repository.Address);
        Assert.Equal(result.UserId, fixture.Repository.Profile!.UserId);
        Assert.Equal(result.UserId, fixture.Repository.Address!.ClienteUserId);
        Assert.True(fixture.Repository.Address.IsDefault);
        Assert.True(fixture.Transaction.Committed);
        Assert.Equal(0, fixture.Transaction.RollbackCount);
        Assert.Equal(
            ["role-check", "begin", "identity-create", "role-add", "profile-add", "address-add", "save", "commit"],
            fixture.Calls);
        Assert.Equal(1, fixture.CpfProtection.FingerprintCalls);
        Assert.Equal(1, fixture.CpfProtection.ProtectCalls);
    }

    [Fact]
    public async Task ExecuteAsync_ComDadosInvalidosNaoVerificaRoleNemIniciaTransacao()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(With(ValidInput(), cpf: "invalido"));

        Assert.Equal(CadastroClienteStatus.ValidationFailed, result.Status);
        Assert.Single(result.ValidationErrors);
        Assert.Empty(fixture.Calls);
        Assert.Equal(0, fixture.TransactionFactory.BeginCalls);
    }

    [Fact]
    public async Task ExecuteAsync_QuandoPapelClienteNaoExisteNaoIniciaTransacao()
    {
        var fixture = new Fixture();
        fixture.Identity.RoleExists = false;

        var result = await fixture.UseCase.ExecuteAsync(ValidInput());

        Assert.Equal(CadastroClienteStatus.RoleUnavailable, result.Status);
        Assert.Equal(["role-check"], fixture.Calls);
        Assert.Equal(0, fixture.TransactionFactory.BeginCalls);
    }

    [Theory]
    [InlineData(ClienteIdentityOperationStatus.Failed, CadastroClienteStatus.IdentityCreationFailed)]
    [InlineData(ClienteIdentityOperationStatus.DuplicateEmail, CadastroClienteStatus.EmailAlreadyRegistered)]
    [InlineData(ClienteIdentityOperationStatus.PersistenceFailed, CadastroClienteStatus.PersistenceFailed)]
    public async Task ExecuteAsync_QuandoCriacaoIdentityFalhaFazRollback(
        ClienteIdentityOperationStatus identityStatus,
        CadastroClienteStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Identity.CreateResult = identityStatus switch
        {
            ClienteIdentityOperationStatus.DuplicateEmail => ClienteIdentityOperationResult.DuplicateEmail(),
            ClienteIdentityOperationStatus.PersistenceFailed => ClienteIdentityOperationResult.PersistenceFailed(),
            _ => ClienteIdentityOperationResult.Failed()
        };

        var result = await fixture.UseCase.ExecuteAsync(ValidInput());

        Assert.Equal(expectedStatus, result.Status);
        Assert.False(fixture.Transaction.Committed);
        Assert.Equal(1, fixture.Transaction.RollbackCount);
        Assert.Null(fixture.Repository.Profile);
        Assert.Null(fixture.Repository.Address);
        Assert.Equal(["role-check", "begin", "identity-create", "rollback"], fixture.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_QuandoSenhaIdentityRejeitadaRetornaValidacaoEFazRollback()
    {
        var fixture = new Fixture();
        fixture.Identity.CreateResult =
            ClienteIdentityOperationResult.PasswordValidationFailed(["PasswordTooShort"]);

        var result = await fixture.UseCase.ExecuteAsync(ValidInput());

        Assert.Equal(CadastroClienteStatus.PasswordValidationFailed, result.Status);
        Assert.Equal(CadastroClienteFailureCategory.IDENTITY, result.FailureCategory);
        Assert.Equal(CadastroClienteFailureStage.IDENTITY_USER_CREATE, result.FailureStage);
        Assert.Equal(["PasswordTooShort"], result.SafeDiagnosticCodes);
        Assert.False(fixture.Transaction.Committed);
        Assert.Equal(1, fixture.Transaction.RollbackCount);
        Assert.Null(fixture.Repository.Profile);
        Assert.Null(fixture.Repository.Address);
        Assert.Equal(["role-check", "begin", "identity-create", "rollback"], fixture.Calls);
    }

    [Theory]
    [InlineData(ClienteIdentityOperationStatus.Failed, CadastroClienteStatus.RoleAssignmentFailed)]
    [InlineData(ClienteIdentityOperationStatus.PersistenceFailed, CadastroClienteStatus.PersistenceFailed)]
    public async Task ExecuteAsync_QuandoAtribuicaoDePapelFalhaFazRollback(
        ClienteIdentityOperationStatus identityStatus,
        CadastroClienteStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Identity.AddToRoleResult = identityStatus == ClienteIdentityOperationStatus.PersistenceFailed
            ? ClienteIdentityOperationResult.PersistenceFailed()
            : ClienteIdentityOperationResult.Failed();

        var result = await fixture.UseCase.ExecuteAsync(ValidInput());

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(1, fixture.Transaction.RollbackCount);
        Assert.Null(fixture.Repository.Profile);
        Assert.Null(fixture.Repository.Address);
        Assert.Equal(["role-check", "begin", "identity-create", "role-add", "rollback"], fixture.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_QuandoCpfDuplicadoNaPersistenciaFazRollbackControlado()
    {
        var fixture = new Fixture();
        fixture.Transaction.SaveException = new ClienteCpfDuplicadoException();

        var result = await fixture.UseCase.ExecuteAsync(ValidInput());

        Assert.Equal(CadastroClienteStatus.CpfAlreadyRegistered, result.Status);
        Assert.Equal(1, fixture.Transaction.RollbackCount);
        Assert.DoesNotContain("52998224725", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_QuandoPersistenciaFalhaRetornaFalhaSeguraEFazRollback()
    {
        var fixture = new Fixture();
        fixture.Transaction.SaveException = new ClientePersistenceException();

        var result = await fixture.UseCase.ExecuteAsync(ValidInput());

        Assert.Equal(CadastroClienteStatus.PersistenceFailed, result.Status);
        Assert.Null(result.UserId);
        Assert.Equal(1, fixture.Transaction.RollbackCount);
        Assert.DoesNotContain("52998224725", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ValidInput().Senha!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ExcecaoInesperadaNaoViraSucessoEEscopoDesfazTransacao()
    {
        var fixture = new Fixture();
        fixture.Identity.ThrowOnCreate = new InvalidOperationException("unexpected");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.UseCase.ExecuteAsync(ValidInput()));

        Assert.Equal(1, fixture.Transaction.RollbackCount);
    }

    private static CadastroClienteInput ValidInput() => new()
    {
        NomeCompleto = "Ana Maria Silva",
        Cpf = "529.982.247-25",
        Email = "ana@example.com",
        Telefone = "+55 (11) 91234-5678",
        Senha = "Senha forte sem regras extras",
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

    private static CadastroClienteInput With(CadastroClienteInput source, string? cpf) => new()
    {
        NomeCompleto = source.NomeCompleto,
        Cpf = cpf,
        Email = source.Email,
        Telefone = source.Telefone,
        Senha = source.Senha,
        EnderecoInicial = source.EnderecoInicial
    };

    private sealed class Fixture
    {
        public List<string> Calls { get; } = [];
        public FakeIdentityService Identity { get; }
        public FakeRepository Repository { get; }
        public FakeTransaction Transaction { get; }
        public FakeTransactionFactory TransactionFactory { get; }
        public FakeCpfProtectionService CpfProtection { get; }
        public CadastrarCliente UseCase { get; }

        public Fixture()
        {
            Identity = new FakeIdentityService(Calls);
            Repository = new FakeRepository(Calls);
            Transaction = new FakeTransaction(Calls);
            TransactionFactory = new FakeTransactionFactory(Calls, Transaction);
            CpfProtection = new FakeCpfProtectionService();
            UseCase = new CadastrarCliente(
                new CadastroClienteInputValidator(),
                CpfProtection,
                Identity,
                Repository,
                TransactionFactory);
        }
    }

    private sealed class FakeIdentityService(List<string> calls) : IClienteIdentityService
    {
        public bool RoleExists { get; set; } = true;
        public ClienteIdentityOperationResult CreateResult { get; set; } = ClienteIdentityOperationResult.Success();
        public ClienteIdentityOperationResult AddToRoleResult { get; set; } = ClienteIdentityOperationResult.Success();
        public Exception? ThrowOnCreate { get; set; }

        public Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken = default)
        {
            calls.Add("role-check");
            Assert.Equal("CLIENTE", role);
            return Task.FromResult(RoleExists);
        }

        public Task<ClienteIdentityOperationResult> CreateUserAsync(
            Guid userId,
            string email,
            string phoneNumber,
            string password,
            CancellationToken cancellationToken = default)
        {
            calls.Add("identity-create");
            if (ThrowOnCreate is not null)
            {
                throw ThrowOnCreate;
            }

            Assert.Equal("ana@example.com", email);
            Assert.Equal("+55 (11) 91234-5678", phoneNumber);
            Assert.Equal("Senha forte sem regras extras", password);
            return Task.FromResult(CreateResult);
        }

        public Task<ClienteIdentityOperationResult> AddToRoleAsync(
            Guid userId,
            string role,
            CancellationToken cancellationToken = default)
        {
            calls.Add("role-add");
            Assert.Equal("CLIENTE", role);
            return Task.FromResult(AddToRoleResult);
        }
    }

    private sealed class FakeRepository(List<string> calls) : IClienteRepository
    {
        public ClientePerfil? Profile { get; private set; }
        public ClienteEndereco? Address { get; private set; }

        public void Adicionar(ClientePerfil perfil)
        {
            calls.Add("profile-add");
            Profile = perfil;
        }

        public void Adicionar(ClienteEndereco endereco)
        {
            calls.Add("address-add");
            Address = endereco;
        }
    }

    private sealed class FakeTransactionFactory(List<string> calls, FakeTransaction transaction)
        : IClienteTransactionFactory
    {
        public int BeginCalls { get; private set; }

        public Task<IClienteTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            BeginCalls++;
            calls.Add("begin");
            return Task.FromResult<IClienteTransaction>(transaction);
        }
    }

    private sealed class FakeTransaction(List<string> calls) : IClienteTransaction
    {
        public bool Committed { get; private set; }
        public int RollbackCount { get; private set; }
        public Exception? SaveException { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("save");
            if (SaveException is not null)
            {
                throw SaveException;
            }

            return Task.FromResult(2);
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("commit");
            Committed = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("rollback");
            RollbackCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!Committed && RollbackCount == 0)
            {
                RollbackCount++;
                calls.Add("rollback");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeCpfProtectionService : ICpfProtectionService
    {
        public int FingerprintCalls { get; private set; }
        public int ProtectCalls { get; private set; }

        public CpfProtegido Protect(Cpf cpf)
        {
            ProtectCalls++;
            return CreateProtectedCpf();
        }

        public Cpf RecoverForAuthorizedUse(CpfProtegido protectedCpf) =>
            throw new NotSupportedException();

        public string CreateFingerprintForLookup(Cpf cpf)
        {
            FingerprintCalls++;
            return new string('A', 64);
        }
    }

    private static CpfProtegido CreateProtectedCpf() =>
        new(
            Convert.ToBase64String(new byte[11]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[16]),
            new string('A', 64),
            "enc-v1",
            "fp-v1");
}
