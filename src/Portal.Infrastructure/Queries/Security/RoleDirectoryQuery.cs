using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Security;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Queries.Security;

public sealed class RoleDirectoryQuery : IRoleDirectoryQuery
{
    private readonly PortalDbContext _dbContext;
    private readonly ICurrentUser _currentUser;

    private sealed record RoleDefinition(
        string Name,
        string DisplayName,
        string Description,
        bool AccessConfigured);

    private static readonly RoleDefinition[] Definitions =
    {
        new(
            AppRoles.Admin,
            "Administrador del sistema",
            "Administra accesos y supervisa la configuración del portal. " +
            "En esta etapa conserva también sus permisos comerciales actuales.",
            true),

        new(
            AppRoles.Seller,
            "Vendedor",
            "Prepara, envía y consulta sus pedidos. " +
            "Los clientes elegibles todavía no se restringen por cartera.",
            true),

     new(
    AppRoles.SalesSupervisor,
    "Supervisor comercial",
    "Consulta sus equipos y administra sus vendedores. " +
    "Consulta los pedidos atribuidos a los equipos activos que supervisa.",
    true),

        new(
            AppRoles.Operator,
            "Operador de integración",
            "Consulta las integraciones y realiza seguimiento técnico. " +
            "Consulta auditoría y solicita recuperaciones con motivo e historial.",
            true),

        new(
            AppRoles.CommercialManagement,
            "Gerencia comercial",
            "Consultará indicadores y resultados comerciales " +
            "con alcance global.",
            false),

        new(
            AppRoles.Auditor,
            "Auditor",
            "Consultará la trazabilidad y los eventos autorizados " +
            "sin modificar las operaciones.",
            false)
    };

    public RoleDirectoryQuery(
        PortalDbContext dbContext,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<RoleDirectoryItemDto>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);

        var registeredRoles = await _dbContext.Roles
            .AsNoTracking()
            .Select(role => new
            {
                role.Id,
                role.Name
            })
            .ToListAsync(cancellationToken);

        var assignments = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking()
                on userRole.UserId equals user.Id
            group user by userRole.RoleId
            into roleGroup
            select new
            {
                RoleId = roleGroup.Key,
                Total = roleGroup.Count(),
                Active = roleGroup.Sum(
                    user => user.IsActive ? 1 : 0)
            })
            .ToListAsync(cancellationToken);

        var assignmentCounts = assignments.ToDictionary(
            item => item.RoleId);

        var items = new List<RoleDirectoryItemDto>();

        foreach (RoleDefinition definition in Definitions)
        {
            var registeredRole = registeredRoles.FirstOrDefault(
                role => string.Equals(
                    role.Name,
                    definition.Name,
                    StringComparison.OrdinalIgnoreCase));

            int totalUsers = 0;
            int activeUsers = 0;

            if (registeredRole is not null &&
                assignmentCounts.TryGetValue(
                    registeredRole.Id,
                    out var counts))
            {
                totalUsers = counts.Total;
                activeUsers = counts.Active;
            }

            items.Add(new RoleDirectoryItemDto
            {
                Name = definition.Name,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                IsRegistered = registeredRole is not null,
                IsRecognized = true,
                AccessConfigured = definition.AccessConfigured,
                AssignedUserCount = totalUsers,
                ActiveAssignedUserCount = activeUsers
            });
        }

        // Mostrar también roles existentes en Identity que no estén
        // definidos en el catálogo de esta versión del portal.
        var knownNames = new HashSet<string>(
            Definitions.Select(definition => definition.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var registeredRole in registeredRoles
                     .OrderBy(role => role.Name))
        {
            string roleName = registeredRole.Name ?? string.Empty;

            if (knownNames.Contains(roleName))
            {
                continue;
            }

            int totalUsers = 0;
            int activeUsers = 0;

            if (assignmentCounts.TryGetValue(
                    registeredRole.Id,
                    out var counts))
            {
                totalUsers = counts.Total;
                activeUsers = counts.Active;
            }

            items.Add(new RoleDirectoryItemDto
            {
                Name = roleName,
                DisplayName = string.IsNullOrWhiteSpace(roleName)
                    ? "Rol sin nombre"
                    : roleName,
                Description =
                    "Existe en Identity, pero no forma parte del " +
                    "catálogo de roles definido para esta versión.",
                IsRegistered = true,
                IsRecognized = false,
                AccessConfigured = false,
                AssignedUserCount = totalUsers,
                ActiveAssignedUserCount = activeUsers
            });
        }

        return items;
    }

    private async Task EnsureAdministratorAsync(
        CancellationToken cancellationToken)
    {
        string? userId = _currentUser.UserId;

        if (!_currentUser.IsAuthenticated ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException(
                "No existe una sesión autorizada.");
        }

        // Comprobar también el estado y la asignación actual
        // en la base de datos.
        bool isActiveAdministrator = await (
            from user in _dbContext.Users.AsNoTracking()
            join userRole in _dbContext.UserRoles.AsNoTracking()
                on user.Id equals userRole.UserId
            join role in _dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where user.Id == userId
                && user.IsActive
                && role.Name == AppRoles.Admin
            select user.Id)
            .AnyAsync(cancellationToken);

        if (!isActiveAdministrator)
        {
            throw new UnauthorizedAccessException(
                "No tienes autorización para consultar los roles.");
        }
    }
}