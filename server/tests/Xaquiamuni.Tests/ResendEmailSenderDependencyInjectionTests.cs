using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Outbox;

namespace Xaquiamuni.Tests;

public sealed class ResendEmailSenderDependencyInjectionTests
{
    private const string SyntheticApiKey = "re_di_test_key";
    private const string SyntheticFromEmail = "no-reply@example.test";

    [Fact]
    public void AddInfrastructureServices_WhenBothFeaturesAreDisabledDoesNotRegisterSenderOrWorker()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices(CreateConfiguration(resendEnabled: false, workerEnabled: false));

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IEmailSender));
        using var provider = services.BuildServiceProvider();
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service is EmailOutboxBackgroundService);
        Assert.False(
            provider.GetRequiredService<IOptions<EmailOutboxBackgroundServiceOptions>>().Value.Enabled);
        Assert.False(new ResendEmailOptions().Enabled);
    }

    [Theory]
    [InlineData(null, SyntheticFromEmail)]
    [InlineData(SyntheticApiKey, null)]
    public async Task AddInfrastructureServices_WhenResendIsEnabledWithoutRequiredOptionsFailsAtStartup(
        string? apiKey,
        string? fromEmail)
    {
        var configuration = CreateConfiguration(
            resendEnabled: true,
            workerEnabled: false,
            apiKey,
            fromEmail);
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddInfrastructureServices(configuration);
            })
            .Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());

        Assert.DoesNotContain(SyntheticApiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticFromEmail, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructureServices_WhenWorkerIsEnabledWithoutSenderFailsClearly()
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructureServices(
                CreateConfiguration(resendEnabled: false, workerEnabled: true)));

        Assert.Equal("The email outbox worker requires a configured email sender.", exception.Message);
        Assert.DoesNotContain(SyntheticApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructureServices_WhenOnlyResendIsEnabledRegistersSenderButNotWorker()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices(
            CreateConfiguration(
                resendEnabled: true,
                workerEnabled: false,
                SyntheticApiKey,
                SyntheticFromEmail));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ResendEmailSender>(provider.GetRequiredService<IEmailSender>());
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service is EmailOutboxBackgroundService);
        Assert.False(
            provider.GetRequiredService<IOptions<EmailOutboxBackgroundServiceOptions>>().Value.Enabled);
        Assert.True(provider.GetRequiredService<IOptions<ResendEmailOptions>>().Value.Enabled);
    }

    [Fact]
    public void AddInfrastructureServices_WhenBothFeaturesAreEnabledRegistersBothIndependently()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices(
            CreateConfiguration(
                resendEnabled: true,
                workerEnabled: true,
                SyntheticApiKey,
                SyntheticFromEmail));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ResendEmailSender>(provider.GetRequiredService<IEmailSender>());
        Assert.Single(provider.GetServices<IHostedService>()
            .OfType<EmailOutboxBackgroundService>());
        Assert.True(provider.GetRequiredService<IOptions<ResendEmailOptions>>().Value.Enabled);
        Assert.True(
            provider.GetRequiredService<IOptions<EmailOutboxBackgroundServiceOptions>>().Value.Enabled);
    }

    private static IConfiguration CreateConfiguration(
        bool resendEnabled,
        bool workerEnabled,
        string? apiKey = null,
        string? fromEmail = null)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{ResendEmailOptions.SectionName}:Enabled"] = resendEnabled.ToString(),
            ["EmailOutboxWorker:Enabled"] = workerEnabled.ToString()
        };
        if (apiKey is not null)
        {
            values[$"{ResendEmailOptions.SectionName}:ApiKey"] = apiKey;
        }

        if (fromEmail is not null)
        {
            values[$"{ResendEmailOptions.SectionName}:FromEmail"] = fromEmail;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
