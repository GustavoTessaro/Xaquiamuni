using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Xaquiamuni.Api.Autenticacao;

public static class ResendConfirmationEmailRateLimiting
{
    public const string PolicyName = "resend-confirmation-email";
    public const string ForgotPasswordPolicyName = "forgot-password";
    public const string ResetPasswordPolicyName = "reset-password";

    private const string UnavailableIpPartition = "ip-unavailable";

    public static IServiceCollection AddResendConfirmationEmailRateLimiting(
        this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    context.HttpContext.Response.Headers.RetryAfter =
                        retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Muitas solicitações. Tente novamente mais tarde." },
                    cancellationToken).ConfigureAwait(false);
            };

            options.AddPolicy(PolicyName, httpContext =>
                CreatePartition(httpContext, PolicyName));
            options.AddPolicy(ForgotPasswordPolicyName, httpContext =>
                CreatePartition(httpContext, ForgotPasswordPolicyName));
            options.AddPolicy(ResetPasswordPolicyName, httpContext =>
                CreatePartition(httpContext, ResetPasswordPolicyName));
        });

        return services;
    }

    private static RateLimitPartition<string> CreatePartition(
        HttpContext context,
        string policyName) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{policyName}:{GetIpPartitionKey(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });

    private static string GetIpPartitionKey(HttpContext context)
    {
        IPAddress? remoteIpAddress = context.Connection.RemoteIpAddress;
        if (remoteIpAddress is null)
        {
            return UnavailableIpPartition;
        }

        if (remoteIpAddress.IsIPv4MappedToIPv6)
        {
            remoteIpAddress = remoteIpAddress.MapToIPv4();
        }

        return $"ip:{remoteIpAddress}";
    }
}
