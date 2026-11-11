namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityRoleInitializer
{
    private readonly IRoleManagerOperations _roleManager;

    public IdentityRoleInitializer(IRoleManagerOperations roleManager)
    {
        _roleManager = roleManager;
    }

    public async Task EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in GetRoleNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
            {
                continue;
            }

            var result = await _roleManager.CreateAsync(roleName).ConfigureAwait(false);
            if (result == RoleCreationStatus.Created)
            {
                continue;
            }

            if (result == RoleCreationStatus.AlreadyExists
                && await _roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
            {
                continue;
            }

            throw new IdentityRoleInitializationException();
        }
    }

    private static IReadOnlyList<string> GetRoleNames() =>
    [
        XaquiamuniRoles.Administrador,
        XaquiamuniRoles.Funcionario,
        XaquiamuniRoles.Cliente
    ];
}

public sealed class IdentityRoleInitializationException : Exception
{
    public IdentityRoleInitializationException()
        : base("Não foi possível garantir a existência dos papéis Identity configurados.")
    {
    }
}
