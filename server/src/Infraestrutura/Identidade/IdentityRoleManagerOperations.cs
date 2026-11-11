using Microsoft.AspNetCore.Identity;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityRoleManagerOperations : IRoleManagerOperations
{
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public IdentityRoleManagerOperations(RoleManager<IdentityRole<Guid>> roleManager)
    {
        _roleManager = roleManager;
    }

    public Task<bool> RoleExistsAsync(string roleName) =>
        _roleManager.RoleExistsAsync(roleName);

    public async Task<RoleCreationStatus> CreateAsync(string roleName)
    {
        var result = await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName))
            .ConfigureAwait(false);
        if (result.Succeeded)
        {
            return RoleCreationStatus.Created;
        }

        return result.Errors.Any(error => error.Code == "DuplicateRoleName")
            ? RoleCreationStatus.AlreadyExists
            : RoleCreationStatus.Failed;
    }
}
