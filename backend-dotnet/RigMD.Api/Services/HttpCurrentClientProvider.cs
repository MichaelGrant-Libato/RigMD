using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using RigMD.Application.Contracts.Common;

namespace RigMD.Api.Services;

public class HttpCurrentClientProvider : ICurrentClientProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentClientProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCurrentClientId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("A local desktop session is required.");

        return httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("The local session identity is missing.");
    }
}
