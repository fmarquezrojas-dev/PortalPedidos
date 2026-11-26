using Microsoft.EntityFrameworkCore;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Services.Customers;

internal static class PortfolioLocks
{
    internal static async Task ForCustomerAsync(PortalDbContext db, Guid customerId, bool exclusive, CancellationToken ct)
    {
        string customer = $"PortalPedidos.CustomerPortfolio:{customerId:N}";
        string mode = exclusive ? "Exclusive" : "Shared";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'PortalPedidos.UserAdministration', @LockMode='Shared', @LockOwner='Transaction', @LockTimeout=5000;
            IF @r < 0 THROW 51080, 'No se pudo bloquear la cuenta.', 1;
            EXEC @r=sys.sp_getapplock @Resource=N'PortalPedidos.SalesTeams', @LockMode='Shared', @LockOwner='Transaction', @LockTimeout=5000;
            IF @r < 0 THROW 51080, 'No se pudo bloquear el equipo.', 1;
            EXEC @r=sys.sp_getapplock @Resource={customer}, @LockMode={mode}, @LockOwner='Transaction', @LockTimeout=5000;
            IF @r < 0 THROW 51080, 'No se pudo bloquear la cartera.', 1;
            """, ct);
    }
}
