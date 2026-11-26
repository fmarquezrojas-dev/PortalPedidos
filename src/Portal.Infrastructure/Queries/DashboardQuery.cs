using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Dashboard;
using Portal.Application.Interfaces.Dashboard;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Enums;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Queries.Integration;
using Portal.Infrastructure.Queries.Security;

namespace Portal.Infrastructure.Queries.Dashboard;

public class DashboardQuery : IDashboardQuery
{
    private static readonly TimeSpan CatalogMaximumAge =
        TimeSpan.FromMinutes(5);

    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DashboardQuery(
        PortalDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<DashboardDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        // La autorización y el alcance se resuelven antes
        // de calcular cualquier indicador de pedidos.
        var ordersQuery = OrderVisibility.ForUser(
            _db,
            _currentUser);

        DateTime now = DateTime.UtcNow;

        bool showsAllOrders =
            _currentUser.CanViewAllOrders;

        bool showsTeamOrders =
            !showsAllOrders &&
            _currentUser.CanViewTeamOrders;

        // Contar únicamente los pedidos del alcance autorizado.
        var orderCounts = await ordersQuery
            .GroupBy(order => order.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        int CountOrders(OrderStatus status)
        {
            return orderCounts
                .Where(item => item.Status == status)
                .Sum(item => item.Count);
        }

        // Utilizar el mismo alcance para los últimos pedidos.
        var recentOrders = await ordersQuery
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.PublicNumber)
            .Take(10)
            .Select(order => new DashboardOrderDto
            {
                Id = order.Id,
                PublicNumber = order.PublicNumber,
                CustomerName = order.CustomerName,
                Status = order.Status,
                Currency = order.Currency,
                Subtotal = order.Subtotal,
                CreatedAtUtc = order.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // El catálogo continúa siendo compartido por el portal.
        var catalog = await _db.CatalogSyncStates
            .AsNoTracking()
            .Where(state => state.Id == 1)
            .Select(state => new
            {
                state.CurrentVersion,
                state.LastSuccessAtUtc,
                state.LastErrorCode
            })
            .SingleOrDefaultAsync(cancellationToken);

        DateTime? lastCatalogSuccess = null;

        if (catalog?.LastSuccessAtUtc is DateTime lastSuccess)
        {
            // SQL Server datetime2 no conserva DateTimeKind.
            // Estas fechas se almacenan en UTC.
            lastCatalogSuccess = DateTime.SpecifyKind(
                lastSuccess,
                DateTimeKind.Utc);
        }

        bool catalogAvailable =
            catalog is not null &&
            catalog.CurrentVersion > 0 &&
            lastCatalogSuccess.HasValue;

        TimeSpan? catalogAge = lastCatalogSuccess.HasValue
            ? now - lastCatalogSuccess.Value
            : null;

        bool invalidFutureDate =
            lastCatalogSuccess.HasValue &&
            lastCatalogSuccess.Value > now.AddMinutes(1);

        bool catalogIsStale =
            !catalogAvailable ||
            invalidFutureDate ||
            (
                catalogAge.HasValue &&
                catalogAge.Value > CatalogMaximumAge
            );

        int? catalogAgeMinutes = catalogAge.HasValue
            ? (int)Math.Clamp(
                Math.Floor(catalogAge.Value.TotalMinutes),
                0d,
                int.MaxValue)
            : null;

        var model = new DashboardDto
        {
            ShowsAllOrders = showsAllOrders,
            ShowsTeamOrders = showsTeamOrders,
            CanCreateOrders = _currentUser.CanCreateOrders,

            GeneratedAtUtc = now,

            TotalOrders =
                orderCounts.Sum(item => item.Count),

            DraftOrders =
                CountOrders(OrderStatus.Draft),

            QueuedOrders =
                CountOrders(OrderStatus.Queued),

            ConfirmedOrders =
                CountOrders(OrderStatus.Confirmed),

            RejectedOrders =
                CountOrders(OrderStatus.Rejected),

            CancelledOrders =
                CountOrders(OrderStatus.Cancelled),

            CatalogVersion =
                catalog?.CurrentVersion ?? 0,

            CatalogLastSuccessAtUtc =
                lastCatalogSuccess,

            // Los detalles técnicos se entregan únicamente
            // a los usuarios con acceso general.
            CatalogLastErrorCode =
                showsAllOrders
                    ? catalog?.LastErrorCode
                    : null,

            CatalogAvailable = catalogAvailable,
            CatalogIsStale = catalogIsStale,
            CatalogAgeMinutes = catalogAgeMinutes,

            RecentOrders = recentOrders
        };

        // En la configuración actual, el acceso general
        // corresponde a operador y administrador.
        //
        // Supervisar equipos no habilita los indicadores
        // técnicos globales de integración.
        if (showsAllOrders)
        {
            var messagesQuery = _db.OutboxMessages
                .AsNoTracking();

            var integrationCounts = await messagesQuery
                .GroupBy(message => message.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken);

            int CountMessages(IntegrationStatus status)
            {
                return integrationCounts
                    .Where(item => item.Status == status)
                    .Sum(item => item.Count);
            }

            model.ActiveIntegrationMessages =
                CountMessages(IntegrationStatus.Pending) +
                CountMessages(IntegrationStatus.Processing) +
                CountMessages(IntegrationStatus.RetryWaiting) +
                CountMessages(IntegrationStatus.Unknown);

            model.UnknownIntegrationMessages =
                CountMessages(IntegrationStatus.Unknown);

            model.DeadLetterIntegrationMessages =
                CountMessages(IntegrationStatus.DeadLetter);

            var unresolvedQuery = messagesQuery.Where(message =>
                message.Status == IntegrationStatus.Pending ||
                message.Status == IntegrationStatus.Processing ||
                message.Status == IntegrationStatus.RetryWaiting ||
                message.Status == IntegrationStatus.Unknown ||
                message.Status == IntegrationStatus.DeadLetter);

            DateTime warningThreshold =
                now - OperationalThresholds.PendingWarningAge;

            model.OverdueIntegrationMessages =
                await unresolvedQuery.CountAsync(
                    message =>
                        message.CreatedAtUtc < warningThreshold,
                    cancellationToken);

            model.OldestUnresolvedMessageAtUtc =
                await unresolvedQuery
                    .Select(message =>
                        (DateTime?)message.CreatedAtUtc)
                    .MinAsync(cancellationToken);
        }

        return model;
    }
}
