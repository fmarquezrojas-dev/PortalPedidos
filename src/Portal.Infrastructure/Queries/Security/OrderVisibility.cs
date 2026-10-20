using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Queries.Security;

internal static class OrderVisibility
{
    public static IQueryable<SalesOrder> ForUser(
        PortalDbContext dbContext,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(currentUser);

        string? userId = currentUser.UserId;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CanViewOrders ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException(
                "Se requiere un usuario autorizado para consultar pedidos.");
        }

        IQueryable<SalesOrder> query =
            dbContext.SalesOrders.AsNoTracking();

        // Administrador y operador mantienen el acceso general.
        if (currentUser.CanViewAllOrders)
        {
            return query;
        }

        if (currentUser.CanViewTeamOrders)
        {
            // El supervisor consulta sus pedidos propios y los
            // atribuidos a los equipos activos que supervisa.
            return query.Where(order =>
                order.OwnerUserId == userId ||
                (
                    order.SalesTeamId.HasValue &&
                    dbContext.SalesTeams.Any(team =>
                        team.Id == order.SalesTeamId.Value &&
                        team.IsActive &&
                        team.SupervisorUserId == userId)
                ));
        }

        // Los demás usuarios autorizados consultan sus pedidos.
        return query.Where(order =>
            order.OwnerUserId == userId);
    }
}