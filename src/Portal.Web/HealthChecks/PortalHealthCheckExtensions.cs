using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Portal.Web.HealthChecks;
public static class PortalHealthCheckExtensions
{
    public static IServiceCollection AddPortalHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<PortalDatabaseHealthCheck>("portal_database",
            failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" }, timeout: TimeSpan.FromSeconds(10));
        return services;
    }
    public static IEndpointRouteBuilder MapPortalHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, AllowCachingResponses = false }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready"), AllowCachingResponses = false }).AllowAnonymous();
        return endpoints;
    }
}
