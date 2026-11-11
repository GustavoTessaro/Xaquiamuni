using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Xaquiamuni.Infraestrutura.Identidade;

public sealed class XaquiamuniEmailConfirmationTokenProvider
    : DataProtectorTokenProvider<XaquiamuniUser>
{
    public const string ProviderName = "XaquiamuniEmailConfirmation";

    public XaquiamuniEmailConfirmationTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<XaquiamuniEmailConfirmationTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<XaquiamuniUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}

public sealed class XaquiamuniEmailConfirmationTokenProviderOptions
    : DataProtectionTokenProviderOptions
{
}
