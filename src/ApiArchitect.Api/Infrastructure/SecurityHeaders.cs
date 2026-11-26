namespace ApiArchitect.Api.Infrastructure;

public static class SecurityHeaders
{
    /// <summary>Blocks MIME sniffing, framing and referrer leaks. No Content-Security-Policy yet.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            await next(context);
        });
}
