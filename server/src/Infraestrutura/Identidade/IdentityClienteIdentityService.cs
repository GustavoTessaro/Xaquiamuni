using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityClienteIdentityService : IClienteIdentityService
{
    private static readonly string[] EmailIndexNames = ["EmailIndex", "UserNameIndex"];

    private readonly UserManager<XaquiamuniUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public IdentityClienteIdentityService(
        UserManager<XaquiamuniUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _roleManager.RoleExistsAsync(role);
    }

    public async Task<ClienteIdentityOperationResult> CreateUserAsync(
        Guid userId,
        string email,
        string phoneNumber,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new XaquiamuniUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            PhoneNumber = phoneNumber
        };

        try
        {
            var result = await _userManager.CreateAsync(user, password).ConfigureAwait(false);
            return MapIdentityResult(result);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception))
        {
            return ClienteIdentityOperationResult.DuplicateEmail();
        }
        catch (DbUpdateException)
        {
            return ClienteIdentityOperationResult.PersistenceFailed();
        }
    }

    public async Task<ClienteIdentityOperationResult> AddToRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
            if (user is null)
            {
                return ClienteIdentityOperationResult.Failed();
            }

            var result = await _userManager.AddToRoleAsync(user, role).ConfigureAwait(false);
            return MapIdentityResult(result);
        }
        catch (DbUpdateException)
        {
            return ClienteIdentityOperationResult.PersistenceFailed();
        }
    }

    private static ClienteIdentityOperationResult MapIdentityResult(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return ClienteIdentityOperationResult.Success();
        }

        return result.Errors.Any(error =>
                error.Code is "DuplicateEmail" or "DuplicateUserName")
            ? ClienteIdentityOperationResult.DuplicateEmail()
            : ClienteIdentityOperationResult.Failed();
    }

    private static bool IsDuplicateEmail(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(error =>
                    (error.Number is 2601 or 2627)
                    && EmailIndexNames.Any(indexName =>
                        error.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }
}
