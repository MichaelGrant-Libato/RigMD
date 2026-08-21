using Microsoft.AspNetCore.Http;
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
        if (httpContext == null) return string.Empty;

        if (httpContext.Request.Headers.TryGetValue("X-Client-ID", out var headerVal) && 
            !string.IsNullOrWhiteSpace(headerVal))
        {
            return headerVal.ToString().Trim();
        }

        if (httpContext.Items.TryGetValue("ClientId", out var itemVal) && itemVal != null)
        {
            return itemVal.ToString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }
}
