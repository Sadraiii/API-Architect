namespace ApiArchitect.Api.Infrastructure;

public static class SecurityHeaders
{
    /// <summary>
    /// Headers that cost nothing and close common holes: MIME sniffing, clickjacking and referrer leaks.
    /// A Content-Security-Policy comes later, once Scalar and Monaco set what scripts the app needs.
    /// </summary>
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
