using Microsoft.AspNetCore.Identity;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityAdministradorLoginService : IAdministradorLoginIdentityService
{
    private readonly UserManager<XaquiamuniUser> _userManager;
    private readonly XaquiamuniSignInManager _signInManager;

    public IdentityAdministradorLoginService(
        UserManager<XaquiamuniUser> userManager,
        XaquiamuniSignInManager signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<LoginAdministradorStatus> SignInAdministradorAsync(
        string email,
        string senha,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null
            || !await _userManager.IsInRoleAsync(user, XaquiamuniRoles.Administrador).ConfigureAwait(false))
        {
            return LoginAdministradorStatus.InvalidCredentials;
        }

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            senha,
            lockoutOnFailure: true).ConfigureAwait(false);
        if (!passwordResult.Succeeded)
        {
            return LoginAdministradorStatus.InvalidCredentials;
        }

        var signInResult = await _signInManager.SignInAfterPasswordCheckAsync(
            user,
            isPersistent: false).ConfigureAwait(false);

        return signInResult.Succeeded
            ? LoginAdministradorStatus.Succeeded
            : LoginAdministradorStatus.InvalidCredentials;
    }
}
