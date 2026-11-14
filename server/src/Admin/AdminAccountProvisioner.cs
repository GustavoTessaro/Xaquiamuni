using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Admin;

public interface IAdminAccountProvisioner
{
    Task<AdminAccountProvisioningStatus> ProvisionAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default);
}

public enum AdminAccountProvisioningStatus
{
    Created,
    InvalidInput,
    IdentityRejected,
    RoleAssignmentFailed
}

public sealed class AdminAccountProvisioner : IAdminAccountProvisioner
{
    private readonly UserManager<XaquiamuniUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly XaquiamuniDbContext _dbContext;

    public AdminAccountProvisioner(
        UserManager<XaquiamuniUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        XaquiamuniDbContext dbContext)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
    }

    public async Task<AdminAccountProvisioningStatus> ProvisionAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(email)
            || email.Trim().Length > 256
            || string.IsNullOrWhiteSpace(password))
        {
            return AdminAccountProvisioningStatus.InvalidInput;
        }

        if (!await _roleManager.RoleExistsAsync(XaquiamuniRoles.Administrador).ConfigureAwait(false))
        {
            return AdminAccountProvisioningStatus.RoleAssignmentFailed;
        }

        var normalizedEmail = email.Trim();
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var user = new XaquiamuniUser
        {
            Email = normalizedEmail,
            UserName = normalizedEmail
        };
        var createResult = await _userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            return AdminAccountProvisioningStatus.IdentityRejected;
        }

        var roleResult = await _userManager.AddToRoleAsync(user, XaquiamuniRoles.Administrador)
            .ConfigureAwait(false);
        if (!roleResult.Succeeded)
        {
            return AdminAccountProvisioningStatus.RoleAssignmentFailed;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return AdminAccountProvisioningStatus.Created;
    }
}
