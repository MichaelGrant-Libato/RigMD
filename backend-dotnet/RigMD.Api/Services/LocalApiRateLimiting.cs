using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Globalization;

namespace RigMD.Api.Services;

public static class LocalApiRateLimiting
{
    public static IServiceCollection AddLocalApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retrySeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : 30;
                context.HttpContext.Response.Headers["Retry-After"] = retrySeconds.ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    code = "local_rate_limit",
                    retryAfterSeconds = retrySeconds,
                    detail = $"Too many local requests. Wait {retrySeconds} seconds before trying again."
                }, cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var path = httpContext.Request.Path.Value?.TrimEnd('/').ToLowerInvariant() ?? string.Empty;
                if (!path.StartsWith("/api/")) return RateLimitPartition.GetNoLimiter("static-or-hub");

                var clientKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "local-client";
                var readOnly = HttpMethods.IsGet(httpContext.Request.Method) || HttpMethods.IsHead(httpContext.Request.Method);
                // Checking saved evidence does not inspect Windows or prepare/execute an action.
                var eligibilityRead = readOnly &&
                    path.Split('/') is ["", "api", "autonomy", var sessionId, "remediation-eligibility"] &&
                    Guid.TryParse(sessionId, out _);
                var metadataRead = readOnly && (path == "/api/autonomy/settings" || path == "/api/autonomy/tools" || eligibilityRead);
                // Navigation must not spend scan/action permits or prevent signing out.
                var group = path switch
                {
                    "/api/cloud-ai/sign-in" or "/api/cloud-ai/connect" when !readOnly => "sign-in",
                    "/api/cloud-ai/sign-out" when !readOnly => "sign-out",
                    "/api/autonomy/settings" when !readOnly => "settings",
                    "/api/diagnosis/local-scan" or "/api/diagnosis/submit" or "/api/diagnosis/analyze" when !readOnly => "diagnosis",
                    _ when !readOnly && path.StartsWith("/api/diagnosis/") && path.EndsWith("/check-resolution") => "diagnosis",
                    _ when path.StartsWith("/api/autonomy/") && !metadataRead => "autonomy",
                    _ when path.StartsWith("/api/remediation/") => "autonomy",
                    _ => "standard"
                };
                var heavy = group != "standard";
                return RateLimitPartition.GetFixedWindowLimiter($"{group}:{clientKey}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = heavy ? 8 : 45,
                        Window = TimeSpan.FromSeconds(heavy ? 30 : 15),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });
        return services;
    }
}
