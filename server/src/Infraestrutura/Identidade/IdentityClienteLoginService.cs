using Microsoft.AspNetCore.Identity;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class IdentityClienteLoginService : IClienteLoginIdentityService
{
    private readonly UserManager<XaquiamuniUser> _userManager;
    private readonly XaquiamuniSignInManager _signInManager;

    public IdentityClienteLoginService(
        UserManager<XaquiamuniUser> userManager,
        XaquiamuniSignInManager signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<LoginClienteStatus> SignInClienteAsync(
        string email,
        string senha,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null
            || !await _userManager.IsInRoleAsync(user, XaquiamuniRoles.Cliente).ConfigureAwait(false))
        {
            return LoginClienteStatus.InvalidCredentials;
        }

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            senha,
            lockoutOnFailure: true).ConfigureAwait(false);
        if (!passwordResult.Succeeded)
        {
            return LoginClienteStatus.InvalidCredentials;
        }

        if (!await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
        {
            return LoginClienteStatus.EmailNotConfirmed;
        }

        var signInResult = await _signInManager.SignInAfterPasswordCheckAsync(
            user,
            isPersistent: false).ConfigureAwait(false);

        return signInResult.Succeeded
            ? LoginClienteStatus.Succeeded
            : LoginClienteStatus.InvalidCredentials;
    }
}
