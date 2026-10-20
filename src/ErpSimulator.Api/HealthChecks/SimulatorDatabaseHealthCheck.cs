using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ErpSimulator.Api.Persistence;
namespace ErpSimulator.Api.HealthChecks;
public sealed class SimulatorDatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopes;
    public SimulatorDatabaseHealthCheck(IServiceScopeFactory scopes) => _scopes = scopes;
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ErpSimulatorDbContext>();
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Base de datos no disponible.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy("No se pudo comprobar la base de datos."); }
    }
}
