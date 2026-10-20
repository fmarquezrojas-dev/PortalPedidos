using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Customers;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Services.Customers;

public sealed class CustomerPortfolioService
    : ICustomerPortfolioService
{
    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomerPortfolioService(
        PortalDbContext db,
        ICurrentUser currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    public async Task<Guid> AssignAsync(
        Guid customerId,
        string sellerEmail,
        string reason,
        Guid? expectedAssignmentId,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        string actorUserId =
            await RequireAdministratorAsync(cancellationToken);

        if (customerId == Guid.Empty)
        {
            throw new CustomerPortfolioException(
                "El cliente seleccionado no es válido.");
        }

        string normalizedEmail =
            sellerEmail?.Trim() ?? string.Empty;

        if (normalizedEmail.Length == 0 ||
            normalizedEmail.Length > 256)
        {
            throw new CustomerPortfolioException(
                "Indica un correo de vendedor válido.");
        }

        string normalizedReason = NormalizeReason(reason);

        ValidateExpectedVersion(
            expectedAssignmentId,
            expectedRowVersion);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);
        // Mantener el mismo orden que la creación de pedidos:
        // catálogo → equipos → cliente.
        await LockAsync(
            "PortalPedidos.CatalogSync",
            "Shared",
            cancellationToken);

        await LockAsync(
            "PortalPedidos.SalesTeams",
            "Shared",
            cancellationToken);

        await LockAsync(
            $"PortalPedidos.CustomerPortfolio:{customerId:N}",
            "Exclusive",
            cancellationToken);

        var customer = await _db.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == customerId,
                cancellationToken);

        if (customer is null || !customer.IsActive)
        {
            throw new CustomerPortfolioException(
                "El cliente no existe o está inactivo.");
        }

        var currentAssignment = await _db.CustomerAssignments
            .SingleOrDefaultAsync(
                item =>
                    item.CustomerId == customerId &&
                    item.EndedAtUtc == null,
                cancellationToken);

        EnsureExpectedAssignment(
            currentAssignment,
            expectedAssignmentId,
            expectedRowVersion);

        var seller = await _userManager.FindByEmailAsync(
            normalizedEmail);

        if (seller is null || !seller.IsActive)
        {
            throw new CustomerPortfolioException(
                "El vendedor no existe o está inactivo.");
        }

        bool hasSellerRole = await _userManager.IsInRoleAsync(
            seller,
            AppRoles.Seller);

        if (!hasSellerRole)
        {
            throw new CustomerPortfolioException(
                "La cuenta seleccionada no tiene el rol Vendedor.");
        }

        var membership = await (
            from member in _db.SalesTeamMembers.AsNoTracking()
            join team in _db.SalesTeams.AsNoTracking()
                on member.SalesTeamId equals team.Id
            where member.UserId == seller.Id
                  && member.LeftAtUtc == null
                  && team.IsActive
            select new
            {
                MembershipId = member.Id,
                TeamId = team.Id
            }
        ).SingleOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            throw new CustomerPortfolioException(
                "El vendedor debe pertenecer a un equipo activo " +
                "antes de recibir clientes.");
        }

        // Guardar el mismo responsable vigente no crea otro historial.
        if (currentAssignment is not null &&
            currentAssignment.SalesTeamMemberId ==
                membership.MembershipId)
        {
            await transaction.CommitAsync(cancellationToken);

            return currentAssignment.Id;
        }

        DateTime now = DateTime.UtcNow;
        Guid correlationId = Guid.NewGuid();

        Guid? previousAssignmentId =
            currentAssignment?.Id;

        Guid? previousMembershipId =
            currentAssignment?.SalesTeamMemberId;

        if (currentAssignment is not null)
        {
            if (now < currentAssignment.AssignedAtUtc)
            {
                throw new CustomerPortfolioException(
                    "La fecha del servidor es anterior a la asignación. " +
                    "Revisa la configuración horaria.");
            }

            currentAssignment.EndedAtUtc = now;
            currentAssignment.EndedByUserId = actorUserId;
            currentAssignment.EndReason = normalizedReason;

            // Cerrar primero la asignación anterior para liberar
            // el índice único de asignaciones abiertas.
            //
            // Si la creación posterior falla, la transacción
            // también revierte este cierre.
            await _db.SaveChangesAsync(cancellationToken);
        }

        var assignment = new CustomerAssignment
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            SalesTeamMemberId = membership.MembershipId,

            AssignedAtUtc = now,
            AssignedByUserId = actorUserId,
            AssignmentReason = normalizedReason
        };

        _db.CustomerAssignments.Add(assignment);

        AddAudit(
            actorUserId,
            currentAssignment is null
                ? "CustomerPortfolio.Assigned"
                : "CustomerPortfolio.Reassigned",
            customerId,
            now,
            correlationId,
            new
            {
                customerId,
                customerCode = customer.ExternalCode,
                previousAssignmentId,
                previousMembershipId,
                assignmentId = assignment.Id,
                membershipId = membership.MembershipId,
                sellerUserId = seller.Id,
                salesTeamId = membership.TeamId,
                reason = normalizedReason
            });

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return assignment.Id;
    }

    public async Task ReleaseAsync(
        Guid customerId,
        Guid expectedAssignmentId,
        byte[] expectedRowVersion,
        string reason,
        CancellationToken cancellationToken = default)
    {
        string actorUserId =
            await RequireAdministratorAsync(cancellationToken);

        if (customerId == Guid.Empty ||
            expectedAssignmentId == Guid.Empty)
        {
            throw new CustomerPortfolioException(
                "El cliente o la asignación no son válidos.");
        }

        string normalizedReason = NormalizeReason(reason);

        ValidateExpectedVersion(
            expectedAssignmentId,
            expectedRowVersion);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        await LockAsync(
            "PortalPedidos.SalesTeams",
            "Shared",
            cancellationToken);

        await LockAsync(
            $"PortalPedidos.CustomerPortfolio:{customerId:N}",
            "Exclusive",
            cancellationToken);

        var currentAssignment = await _db.CustomerAssignments
            .SingleOrDefaultAsync(
                item =>
                    item.CustomerId == customerId &&
                    item.EndedAtUtc == null,
                cancellationToken);

        EnsureExpectedAssignment(
            currentAssignment,
            expectedAssignmentId,
            expectedRowVersion);

        if (currentAssignment is null)
        {
            throw new CustomerPortfolioException(
                "El cliente ya no tiene una asignación abierta. " +
                "Recarga la ficha.");
        }

        DateTime now = DateTime.UtcNow;

        if (now < currentAssignment.AssignedAtUtc)
        {
            throw new CustomerPortfolioException(
                "La fecha del servidor es anterior a la asignación. " +
                "Revisa la configuración horaria.");
        }

        currentAssignment.EndedAtUtc = now;
        currentAssignment.EndedByUserId = actorUserId;
        currentAssignment.EndReason = normalizedReason;

        AddAudit(
            actorUserId,
            "CustomerPortfolio.Released",
            customerId,
            now,
            Guid.NewGuid(),
            new
            {
                customerId,
                assignmentId = currentAssignment.Id,
                membershipId =
                    currentAssignment.SalesTeamMemberId,
                reason = normalizedReason
            });

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<string> RequireAdministratorAsync(
        CancellationToken cancellationToken)
    {
        string? userId = _currentUser.UserId;

        if (!_currentUser.IsAuthenticated ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException(
                "Debes iniciar sesión para gestionar la cartera.");
        }

        // Comprobar cuenta activa y rol actual en la base de datos.
        bool authorized = await (
            from user in _db.Users.AsNoTracking()
            join userRole in _db.UserRoles.AsNoTracking()
                on user.Id equals userRole.UserId
            join role in _db.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where user.Id == userId
                  && user.IsActive
                  && role.Name == AppRoles.Admin
            select user.Id
        ).AnyAsync(cancellationToken);

        if (!authorized)
        {
            throw new UnauthorizedAccessException(
                "Solo el administrador puede modificar " +
                "la cartera de clientes.");
        }

        return userId;
    }

    private static string NormalizeReason(string reason)
    {
        string normalized = reason?.Trim() ?? string.Empty;

        if (normalized.Length < 5 || normalized.Length > 250)
        {
            throw new CustomerPortfolioException(
                "El motivo debe tener entre 5 y 250 caracteres.");
        }

        return normalized;
    }

    private static void ValidateExpectedVersion(
        Guid? expectedAssignmentId,
        byte[]? expectedRowVersion)
    {
        if (!expectedAssignmentId.HasValue)
        {
            if (expectedRowVersion is { Length: > 0 })
            {
                throw new CustomerPortfolioException(
                    "La versión de la asignación no es válida. " +
                    "Recarga la ficha.");
            }

            return;
        }

        if (expectedAssignmentId.Value == Guid.Empty ||
            expectedRowVersion is null ||
            expectedRowVersion.Length != 8)
        {
            throw new CustomerPortfolioException(
                "La versión de la asignación no es válida. " +
                "Recarga la ficha.");
        }
    }

    private static void EnsureExpectedAssignment(
        CustomerAssignment? currentAssignment,
        Guid? expectedAssignmentId,
        byte[]? expectedRowVersion)
    {
        if (currentAssignment is null)
        {
            if (expectedAssignmentId.HasValue)
            {
                throw new CustomerPortfolioException(
                    "La asignación cambió desde que abriste la ficha. " +
                    "Recarga antes de continuar.");
            }

            return;
        }

        if (expectedAssignmentId != currentAssignment.Id ||
            expectedRowVersion is null ||
            !currentAssignment.RowVersion.SequenceEqual(
                expectedRowVersion))
        {
            throw new CustomerPortfolioException(
                "La asignación cambió desde que abriste la ficha. " +
                "Recarga antes de continuar.");
        }
    }

    private async Task LockAsync(
        string resource,
        string mode,
        CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = {mode},
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;

            IF @LockResult < 0
                THROW 51080, 'No se pudo bloquear la operación de cartera.', 1;
            """,
            cancellationToken);
    }

    private void AddAudit(
        string actorUserId,
        string action,
        Guid customerId,
        DateTime occurredAtUtc,
        Guid correlationId,
        object summary)
    {
        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            ActorType = "User",

            Action = action,
            EntityType = "Customer",
            EntityId = customerId.ToString("D"),

            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId,

            SummaryJson = JsonSerializer.Serialize(summary)
        });
    }
}