namespace Xaquiamuni.Aplicacao.Cadastro;

public interface IClienteIdentityService
{
    Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken = default);

    Task<ClienteIdentityOperationResult> CreateUserAsync(
        Guid userId,
        string email,
        string phoneNumber,
        string password,
        CancellationToken cancellationToken = default);

    Task<ClienteIdentityOperationResult> AddToRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);
}
