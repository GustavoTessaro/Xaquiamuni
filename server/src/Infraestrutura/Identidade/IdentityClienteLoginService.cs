using Microsoft.AspNetCore.Identity;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityClienteLoginService : IClienteLoginIdentityService
{
    private readonly UserManager<XaquiamuniUser> _userManager;
    private readonly SignInManager<XaquiamuniUser> _signInManager;

    public IdentityClienteLoginService(
        UserManager<XaquiamuniUser> userManager,
        SignInManager<XaquiamuniUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<bool> SignInClienteAsync(
        string email,
        string senha,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null
            || !await _userManager.IsInRoleAsync(user, XaquiamuniRoles.Cliente).ConfigureAwait(false))
        {
            return false;
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            senha,
            isPersistent: false,
            lockoutOnFailure: true).ConfigureAwait(false);

        return result.Succeeded;
    }
}
