using Saas.Application.Web.Interfaces;

namespace Saas.Application.Web.Middleware;

/// <summary>
/// Blocks end-user access to a tenant whose Azure Marketplace subscription is Suspended or
/// Unsubscribed. The tenant is identified by the <c>route</c> request value (query or route data),
/// the same key the product app already uses to resolve a tenant. Non-marketplace tenants, trials,
/// and unknown/transient states are treated as active (fail-open) so legacy tenants keep working
/// and a flaky Admin API call doesn't lock everyone out.
/// </summary>
public class RequireActiveSubscriptionMiddleware(RequestDelegate next, ILogger<RequireActiveSubscriptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantService tenantService)
    {
        // Only gate authenticated requests that actually target a tenant (carry a route).
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var route = GetTenantRoute(context);

            if (!string.IsNullOrWhiteSpace(route))
            {
                try
                {
                    var tenant = await tenantService.GetTenantInfoByRouteAsync(route);
                    if (tenant.IsAccessBlocked)
                    {
                        logger.LogInformation(
                            "Access blocked for tenant route '{Route}' (subscription status '{Status}').",
                            route, tenant.SubscriptionStatus);

                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsync(
                            "This subscription is not active. Please contact your administrator or check your subscription status in Azure.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Fail open: don't lock users out on a transient resolution error (matches the
                    // Index page, which logs and renders "Unknown" rather than failing the request).
                    logger.LogWarning(ex, "Could not evaluate subscription status for route '{Route}'; allowing the request.", route);
                }
            }
        }

        await next(context);
    }

    private static string? GetTenantRoute(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("route", out var queryRoute) && !string.IsNullOrWhiteSpace(queryRoute))
        {
            return queryRoute.ToString();
        }

        if (context.Request.RouteValues.TryGetValue("route", out var routeValue) && routeValue is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return null;
    }
}
