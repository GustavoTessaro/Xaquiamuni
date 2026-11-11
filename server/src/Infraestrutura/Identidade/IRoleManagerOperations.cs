namespace Xaquiamuni.Infraestrutura.Identidade;

public interface IRoleManagerOperations
{
    Task<bool> RoleExistsAsync(string roleName);

    Task<RoleCreationStatus> CreateAsync(string roleName);
}

public enum RoleCreationStatus
{
    Created,
    AlreadyExists,
    Failed
}
