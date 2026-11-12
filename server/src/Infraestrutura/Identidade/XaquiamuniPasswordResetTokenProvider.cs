using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class XaquiamuniPasswordResetTokenProvider
    : DataProtectorTokenProvider<XaquiamuniUser>
{
    public const string ProviderName = "XaquiamuniPasswordReset";
    public const string ProtectionPurpose = "Xaquiamuni.PasswordReset";

    public XaquiamuniPasswordResetTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<XaquiamuniPasswordResetTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<XaquiamuniUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}

public sealed class XaquiamuniPasswordResetTokenProviderOptions
    : DataProtectionTokenProviderOptions
{
}
