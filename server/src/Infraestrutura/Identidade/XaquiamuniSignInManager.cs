using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Xaquiamuni.Infraestrutura.Identidade;

public class XaquiamuniSignInManager : SignInManager<XaquiamuniUser>
{
    public XaquiamuniSignInManager(
        UserManager<XaquiamuniUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<XaquiamuniUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<XaquiamuniUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<XaquiamuniUser> confirmation)
        : base(
            userManager,
            contextAccessor,
            claimsFactory,
            optionsAccessor,
            logger,
            schemes,
            confirmation)
    {
    }

    internal Task<SignInResult> SignInAfterPasswordCheckAsync(
        XaquiamuniUser user,
        bool isPersistent) =>
        SignInOrTwoFactorAsync(user, isPersistent);
}
