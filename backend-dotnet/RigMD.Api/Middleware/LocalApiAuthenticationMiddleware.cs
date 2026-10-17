using System.Net;
using System.Security.Claims;
using RigMD.Api.Services;

namespace RigMD.Api.Middleware;

public sealed class LocalApiAuthenticationMiddleware
{
    private static readonly HashSet<string> AllowedOrigins = new(StringComparer.Ordinal)
    {
        "http://localhost:5273", "http://127.0.0.1:5273",
        "http://localhost:5173", "http://127.0.0.1:5173"
    };
    private readonly RequestDelegate _next;

    public LocalApiAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, LocalApiSession session)
    {
        var isHub = context.Request.Path.StartsWithSegments("/hubs");
        if (!isHub && !context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        var origin = context.Request.Headers.Origin;
        if (context.Connection.RemoteIpAddress == null ||
            !IPAddress.IsLoopback(context.Connection.RemoteIpAddress) ||
            (origin.Count > 0 && (origin.Count != 1 || !AllowedOrigins.Contains(origin[0] ?? ""))) ||
            string.Equals(context.Request.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { detail = "This connection is not permitted." });
            return;
        }

        var authorization = context.Request.Headers.Authorization;
        string? token = authorization.Count == 1 &&
            authorization[0]?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                ? authorization[0]![7..] : null;

        // Browser WebSocket/SSE transports cannot attach an Authorization header.
        if (token == null && isHub && HttpMethods.IsGet(context.Request.Method) && authorization.Count == 0)
        {
            var queryToken = context.Request.Query["access_token"];
            if (queryToken.Count == 1) token = queryToken[0];
        }

        if (!session.IsValid(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new { detail = "Open RigMD Desktop to connect to local diagnostics." });
            return;
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, LocalApiSession.ClientId) }, "RigMDDesktop"));
        context.Items["ClientId"] = LocalApiSession.ClientId;
        await _next(context);
    }
}
