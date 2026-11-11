using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public class IdentityRoleInitializerTests
{
    [Fact]
    public async Task EnsureRolesAsync_CreatesOnlyMissingConfiguredRoles()
    {
        var roleManager = new FakeRoleManager([XaquiamuniRoles.Cliente]);
        var initializer = new IdentityRoleInitializer(roleManager);

        await initializer.EnsureRolesAsync();

        Assert.Equal(
            [XaquiamuniRoles.Administrador, XaquiamuniRoles.Funcionario],
            roleManager.CreatedRoles);
        Assert.Equal(
            new[] { XaquiamuniRoles.Administrador, XaquiamuniRoles.Funcionario, XaquiamuniRoles.Cliente }
                .Order(StringComparer.Ordinal),
            roleManager.ExistingRoles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EnsureRolesAsync_WhenAllRolesExistDoesNotCreateOrModifyRoles()
    {
        var allRoles = new[]
        {
            XaquiamuniRoles.Administrador,
            XaquiamuniRoles.Funcionario,
            XaquiamuniRoles.Cliente
        };
        var roleManager = new FakeRoleManager(allRoles);
        var initializer = new IdentityRoleInitializer(roleManager);

        await initializer.EnsureRolesAsync();

        Assert.Empty(roleManager.CreatedRoles);
        Assert.Equal(
            allRoles.Order(StringComparer.Ordinal),
            roleManager.ExistingRoles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EnsureRolesAsync_CanRunRepeatedlyWithoutDuplicatingRoles()
    {
        var roleManager = new FakeRoleManager([]);
        var initializer = new IdentityRoleInitializer(roleManager);

        await initializer.EnsureRolesAsync();
        await initializer.EnsureRolesAsync();

        Assert.Equal(3, roleManager.CreatedRoles.Count);
        Assert.Equal(3, roleManager.CreatedRoles.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, roleManager.ExistingRoles.Count);
    }

    [Fact]
    public async Task EnsureRolesAsync_WhenCreationFailsThrowsSafeExplicitException()
    {
        var roleManager = new FakeRoleManager([]) { CreateStatus = RoleCreationStatus.Failed };
        var initializer = new IdentityRoleInitializer(roleManager);

        var exception = await Assert.ThrowsAsync<IdentityRoleInitializationException>(
            () => initializer.EnsureRolesAsync());

        Assert.Equal(
            "Não foi possível garantir a existência dos papéis Identity configurados.",
            exception.Message);
        Assert.DoesNotContain("SQL", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureRolesAsync_ConcurrentDuplicateCreationIsAcceptedOnlyAfterRoleExists()
    {
        var roleManager = new FakeRoleManager([])
        {
            CreateStatus = RoleCreationStatus.AlreadyExists,
            AddOnDuplicate = true
        };
        var initializer = new IdentityRoleInitializer(roleManager);

        await initializer.EnsureRolesAsync();

        Assert.Equal(3, roleManager.ExistingRoles.Count);
    }

    private sealed class FakeRoleManager(IEnumerable<string> existingRoles) : IRoleManagerOperations
    {
        public HashSet<string> ExistingRoles { get; } = new(existingRoles, StringComparer.Ordinal);
        public List<string> CreatedRoles { get; } = [];
        public RoleCreationStatus CreateStatus { get; set; } = RoleCreationStatus.Created;
        public bool AddOnDuplicate { get; set; }

        public Task<bool> RoleExistsAsync(string roleName) =>
            Task.FromResult(ExistingRoles.Contains(roleName));

        public Task<RoleCreationStatus> CreateAsync(string roleName)
        {
            if (CreateStatus == RoleCreationStatus.Created)
            {
                CreatedRoles.Add(roleName);
                ExistingRoles.Add(roleName);
            }
            else if (CreateStatus == RoleCreationStatus.AlreadyExists && AddOnDuplicate)
            {
                ExistingRoles.Add(roleName);
            }

            return Task.FromResult(CreateStatus);
        }
    }
}
