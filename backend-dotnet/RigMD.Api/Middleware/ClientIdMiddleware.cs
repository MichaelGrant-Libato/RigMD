using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace RigMD.Api.Middleware;

public class ClientIdMiddleware
{
    private readonly RequestDelegate _next;

    public ClientIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only enforce on /api routes (ignore swagger, static files, etc)
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            if (!context.Request.Headers.TryGetValue("X-Client-ID", out var clientIdValues) || 
                string.IsNullOrWhiteSpace(clientIdValues.ToString()))
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"detail\":\"X-Client-ID header is required. Each client must send a stable, unique identifier with every request.\"}");
                return;
            }

            // Store the Client ID in HttpContext.Items so downstream services can access it
            context.Items["ClientId"] = clientIdValues.ToString();
        }

        await _next(context);
    }
}
