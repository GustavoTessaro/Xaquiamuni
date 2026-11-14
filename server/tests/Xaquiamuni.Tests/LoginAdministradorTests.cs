using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Tests;

public sealed class LoginAdministradorTests
{
    [Fact]
    public async Task ValidCredentials_TrimEmailAndReturnSuccess()
    {
        var identityService = new FakeAdministratorLoginIdentityService(LoginAdministradorStatus.Succeeded);
        var result = await new LoginAdministrador(identityService).ExecuteAsync(new LoginAdministradorInput
        {
            Email = "  ADMIN@EXEMPLO.TEST ",
            Senha = "SenhaSegura!123"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("ADMIN@EXEMPLO.TEST", identityService.Email);
        Assert.Equal("SenhaSegura!123", identityService.Password);
        Assert.Equal(1, identityService.Calls);
    }

    [Theory]
    [InlineData(null, "SenhaSegura!123")]
    [InlineData(" ", "SenhaSegura!123")]
    [InlineData("admin@exemplo.test", null)]
    [InlineData("admin@exemplo.test", " ")]
    public async Task MissingCredentials_FailWithoutCallingIdentity(string? email, string? password)
    {
        var identityService = new FakeAdministratorLoginIdentityService(LoginAdministradorStatus.Succeeded);
        var result = await new LoginAdministrador(identityService).ExecuteAsync(new LoginAdministradorInput
        {
            Email = email,
            Senha = password
        });

        Assert.False(result.Succeeded);
        Assert.Equal(LoginAdministradorStatus.InvalidCredentials, result.Status);
        Assert.Equal(0, identityService.Calls);
    }

    [Fact]
    public async Task IdentityRejection_ReturnsGenericFailure()
    {
        var identityService = new FakeAdministratorLoginIdentityService(
            LoginAdministradorStatus.InvalidCredentials);
        var result = await new LoginAdministrador(identityService).ExecuteAsync(new LoginAdministradorInput
        {
            Email = "admin@exemplo.test",
            Senha = "SenhaIncorreta!123"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(LoginAdministradorStatus.InvalidCredentials, result.Status);
    }

    private sealed class FakeAdministratorLoginIdentityService(LoginAdministradorStatus status)
        : IAdministradorLoginIdentityService
    {
        public int Calls { get; private set; }
        public string? Email { get; private set; }
        public string? Password { get; private set; }

        public Task<LoginAdministradorStatus> SignInAdministradorAsync(
            string email,
            string senha,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Email = email;
            Password = senha;
            return Task.FromResult(status);
        }
    }
}
