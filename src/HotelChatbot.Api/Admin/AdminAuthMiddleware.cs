namespace HotelChatbot.Api.Admin;

public class AdminAuthMiddleware
{
    private readonly RequestDelegate _next;

    public AdminAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AdminAuthService authService)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/admin/auth", StringComparison.OrdinalIgnoreCase)
            || IsPublicAdminReadEndpoint(context.Request.Method, path))
        {
            await _next(context);
            return;
        }

        var token = ExtractBearerToken(context.Request.Headers.Authorization.ToString());
        if (!authService.TryValidateToken(token, out var role))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Nicht autorisiert." });
            return;
        }

        if (role == AdminRoles.Hotel && IsForbiddenForHotel(context.Request.Method, path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Keine Berechtigung für diese Aktion." });
            return;
        }

        context.Items["AdminRole"] = role;
        await _next(context);
    }

    private static string? ExtractBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return null;

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return authorizationHeader[prefix.Length..].Trim();
    }

    private static bool IsForbiddenForHotel(string method, string path)
    {
        var normalizedMethod = method.ToUpperInvariant();

        if (path.StartsWith("/api/admin/system-prompts", StringComparison.OrdinalIgnoreCase))
            return normalizedMethod != HttpMethods.Get;

        if (normalizedMethod == HttpMethods.Get)
            return false;

        if (normalizedMethod == HttpMethods.Delete
            && path.StartsWith("/api/admin/delete-content", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedMethod == HttpMethods.Put
            && path.StartsWith("/api/admin/hotels/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedMethod == HttpMethods.Patch
            && path.StartsWith("/api/admin/feedback/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/status", StringComparison.OrdinalIgnoreCase))
            return true;

        return path.Equals("/api/admin/crawl-multiple-hotels", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/admin/crawl-multiple-hotels-headless", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/admin/crawl-and-index", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/admin/crawl-preview", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/admin/test-crawler", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/admin/index-content", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lese-Endpunkte, die vom MCP-Server und der Laufzeit ohne Admin-Login erreichbar bleiben müssen.
    /// </summary>
    private static bool IsPublicAdminReadEndpoint(string method, string path)
    {
        if (!string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Equals("/api/admin/system-prompts", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/admin/system-prompts/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("/api/admin/hotels", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/admin/hotels/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
