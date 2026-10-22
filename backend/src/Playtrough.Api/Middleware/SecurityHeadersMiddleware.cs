namespace Playtrough.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        // Register outside exception handling: headers survive Response.Clear(),
        // redirects, preflight short circuits and authentication failures.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            if (!context.Request.Path.StartsWithSegments("/swagger"))
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
