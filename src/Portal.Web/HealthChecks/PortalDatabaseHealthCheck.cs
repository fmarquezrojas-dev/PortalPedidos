using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Portal.Infrastructure.Persistence;
namespace Portal.Web.HealthChecks;
public sealed class PortalDatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopes;
    public PortalDatabaseHealthCheck(IServiceScopeFactory scopes) => _scopes = scopes;
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Base de datos no disponible.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy("No se pudo comprobar la base de datos."); }
    }
}
