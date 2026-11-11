using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;

namespace Xaquiamuni.Infraestrutura.Autenticacao;

public static class ResendEmailSenderServiceCollectionExtensions
{
    public static IServiceCollection AddResendEmailSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ResendEmailOptions>()
            .Bind(configuration.GetSection(ResendEmailOptions.SectionName))
            .Validate(
                options => ResendEmailOptionsValidation.IsApiKeyValid(options.ApiKey),
                "Resend ApiKey is required and must be a single token.")
            .Validate(
                options => ResendEmailOptionsValidation.IsFromEmailValid(options.FromEmail),
                "Resend FromEmail must be a valid email address.")
            .ValidateOnStart();

        services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
            client.Timeout = ResendEmailOptions.RequestTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        return services;
    }
}
