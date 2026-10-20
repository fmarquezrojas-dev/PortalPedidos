using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portal.Application.DTOs.Teams;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Security;
using Portal.Application.Interfaces.Teams;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Services.Teams;


public sealed class SalesTeamService : ISalesTeamService
{
    private const int PageSize = 20;
    private const int MaximumMembers = 50;

    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    private sealed record Actor(
        string UserId,
        bool IsAdmin,
        bool IsSupervisor,
        bool IsSeller);

    public SalesTeamService(
        PortalDbContext db,
        ICurrentUser currentUser,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _db = db;
        _currentUser = currentUser;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<SalesTeamPageDto> GetPageAsync(
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        Actor actor = await GetActorAsync(cancellationToken);

        var query = VisibleTeams(actor);

        int totalCount = await query.CountAsync(cancellationToken);

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)PageSize));

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var items = await query
            .OrderBy(team => team.Code)
            .ThenBy(team => team.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(team => new SalesTeamSummaryDto
            {
                Id = team.Id,
                Code = team.Code,
                Name = team.Name,
                IsActive = team.IsActive,

                SupervisorEmail = _db.Users
                    .Where(user => user.Id == team.SupervisorUserId)
                    .Select(user => user.Email)
                    .FirstOrDefault() ?? string.Empty,

                MemberCount = _db.SalesTeamMembers.Count(member =>
                    member.SalesTeamId == team.Id &&
                    member.LeftAtUtc == null)
            })
            .ToListAsync(cancellationToken);

        return new SalesTeamPageDto
        {
            Items = items,
            CanCreate = actor.IsAdmin,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = PageSize
        };
    }

    public async Task<SalesTeamDetailDto?> GetDetailsAsync(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        Actor actor = await GetActorAsync(cancellationToken);

        var team = await VisibleTeams(actor)
            .SingleOrDefaultAsync(
                item => item.Id == teamId,
                cancellationToken);

        if (team is null)
        {
            return null;
        }

        string supervisorEmail = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == team.SupervisorUserId)
            .Select(user => user.Email ?? string.Empty)
            .SingleAsync(cancellationToken);

        var memberQuery =
            from member in _db.SalesTeamMembers.AsNoTracking()
            join user in _db.Users.AsNoTracking()
                on member.UserId equals user.Id
            where member.SalesTeamId == teamId
            select new SalesTeamMemberDto
            {
                MembershipId = member.Id,
                Email = user.Email ?? string.Empty,
                UserIsActive = user.IsActive,
                JoinedAtUtc = member.JoinedAtUtc,
                LeftAtUtc = member.LeftAtUtc
            };

        var members = await memberQuery
            .Where(member => member.LeftAtUtc == null)
            .OrderBy(member => member.Email)
            .ToListAsync(cancellationToken);

        var departures = await memberQuery
            .Where(member => member.LeftAtUtc != null)
            .OrderByDescending(member => member.LeftAtUtc)
            .ThenByDescending(member => member.MembershipId)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new SalesTeamDetailDto
        {
            Id = team.Id,
            Code = team.Code,
            Name = team.Name,
            IsActive = team.IsActive,
            SupervisorEmail = supervisorEmail,
            MemberCount = members.Count,
            RowVersion = team.RowVersion,
            Members = members,
            RecentDepartures = departures,

            CanManage = team.IsActive &&
                (actor.IsAdmin ||
                 (actor.IsSupervisor &&
                  team.SupervisorUserId == actor.UserId))
        };
    }

    public async Task<Guid> CreateAsync(
        string code,
        string name,
        string supervisorEmail,
        CancellationToken cancellationToken = default)
    {
        code = RequiredText(code, 20, "El código")
            .ToUpperInvariant();

        name = RequiredText(name, 120, "El nombre");

        supervisorEmail = RequiredText(
            supervisorEmail,
            256,
            "El correo del supervisor");

        if (!Regex.IsMatch(code, @"\A[A-Z0-9][A-Z0-9_-]{1,19}\z"))
        {
            throw new SalesTeamException(
                "El código debe tener entre 2 y 20 caracteres. " +
                "Usa letras, números, guion o guion bajo.");
        }

        await using var transaction =
            await BeginWriteAsync(cancellationToken);

        Actor actor = await GetActorAsync(cancellationToken);

        if (!actor.IsAdmin)
        {
            throw new UnauthorizedAccessException();
        }

        bool codeExists = await _db.SalesTeams.AnyAsync(
            team => team.Code == code,
            cancellationToken);

        if (codeExists)
        {
            throw new SalesTeamException(
                "Ya existe un equipo con ese código.");
        }

        var supervisor = await _userManager.FindByEmailAsync(
            supervisorEmail);

        if (supervisor is null || !supervisor.IsActive)
        {
            throw new SalesTeamException(
                "El supervisor debe ser una cuenta existente y activa.");
        }

        bool isAdministrator = await _userManager.IsInRoleAsync(
            supervisor,
            AppRoles.Admin);

        bool isSupervisor = await _userManager.IsInRoleAsync(
            supervisor,
            AppRoles.SalesSupervisor);

        bool roleGranted = false;

        // El administrador ya puede gestionar equipos.
        // Para otras cuentas se asigna expresamente el rol.
        if (!isAdministrator && !isSupervisor)
        {
            bool roleExists = await _roleManager.RoleExistsAsync(
                AppRoles.SalesSupervisor);

            if (!roleExists)
            {
                throw new SalesTeamException(
                    "El rol SupervisorComercial todavía no está registrado. " +
                    "Ejecuta la preparación de roles antes de continuar.");
            }

            IdentityResult roleResult =
                await _userManager.AddToRoleAsync(
                    supervisor,
                    AppRoles.SalesSupervisor);

            if (!roleResult.Succeeded)
            {
                throw new SalesTeamException(
                    "No se pudo asignar el rol al supervisor.");
            }

            IdentityResult stampResult =
                await _userManager.UpdateSecurityStampAsync(supervisor);

            if (!stampResult.Succeeded)
            {
                throw new SalesTeamException(
                    "No se pudo actualizar la sesión del supervisor.");
            }

            roleGranted = true;
        }

        DateTime now = DateTime.UtcNow;

        var team = new SalesTeam
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            SupervisorUserId = supervisor.Id,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.SalesTeams.Add(team);

        AddAudit(
            actor.UserId,
            team.Id,
            "SalesTeam.Created",
            new
            {
                team.Code,
                team.Name,
                team.SupervisorUserId,
                SupervisorRoleGranted = roleGranted
            });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return team.Id;
    }

    public async Task AddMemberAsync(
        Guid teamId,
        string sellerEmail,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        sellerEmail = RequiredText(
            sellerEmail,
            256,
            "El correo del vendedor");

        await using var transaction =
            await BeginWriteAsync(cancellationToken);

        Actor actor = await GetActorAsync(cancellationToken);

        SalesTeam team = await GetManageableTeamAsync(
            teamId,
            actor,
            cancellationToken);

        CheckVersion(team, rowVersion);

        var seller = await _userManager.FindByEmailAsync(sellerEmail);

        if (seller is null || !seller.IsActive)
        {
            throw new SalesTeamException(
                "El vendedor debe ser una cuenta existente y activa.");
        }

        bool isSeller = await _userManager.IsInRoleAsync(
            seller,
            AppRoles.Seller);

        if (!isSeller)
        {
            throw new SalesTeamException(
                "La cuenta seleccionada no tiene el rol Vendedor.");
        }

        bool hasCurrentAssignment =
            await _db.SalesTeamMembers.AnyAsync(
                member =>
                    member.UserId == seller.Id &&
                    member.LeftAtUtc == null,
                cancellationToken);

        if (hasCurrentAssignment)
        {
            throw new SalesTeamException(
                "El vendedor ya tiene una asignación vigente. " +
                "Debe retirarse de su equipo actual antes de incorporarlo.");
        }

        int count = await _db.SalesTeamMembers.CountAsync(
            member =>
                member.SalesTeamId == teamId &&
                member.LeftAtUtc == null,
            cancellationToken);

        if (count >= MaximumMembers)
        {
            throw new SalesTeamException(
                $"El equipo admite un máximo de {MaximumMembers} vendedores.");
        }

        DateTime now = DateTime.UtcNow;

        var membership = new SalesTeamMember
        {
            Id = Guid.NewGuid(),
            SalesTeamId = team.Id,
            UserId = seller.Id,
            JoinedAtUtc = now
        };

        _db.SalesTeamMembers.Add(membership);

        Touch(team, now);

        AddAudit(
            actor.UserId,
            team.Id,
            "SalesTeam.MemberAdded",
            new
            {
                MembershipId = membership.Id,
                SellerUserId = seller.Id
            });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(
        Guid teamId,
        Guid membershipId,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await BeginWriteAsync(cancellationToken);

        Actor actor = await GetActorAsync(cancellationToken);

        SalesTeam team = await GetManageableTeamAsync(
            teamId,
            actor,
            cancellationToken);

        CheckVersion(team, rowVersion);

        var membership = await _db.SalesTeamMembers
            .SingleOrDefaultAsync(
                member =>
                    member.Id == membershipId &&
                    member.SalesTeamId == teamId &&
                    member.LeftAtUtc == null,
                cancellationToken);

        if (membership is null)
        {
            throw new SalesTeamException(
                "La asignación ya no está vigente. Recarga el equipo.");
        }

        DateTime now = DateTime.UtcNow;

        if (now < membership.JoinedAtUtc)
        {
            throw new SalesTeamException(
                "La fecha del servidor es anterior al ingreso registrado. " +
                "No se pudo finalizar la asignación.");
        }

        membership.LeftAtUtc = now;

        Touch(team, now);

        AddAudit(
            actor.UserId,
            team.Id,
            "SalesTeam.MemberRemoved",
            new
            {
                MembershipId = membership.Id,
                SellerUserId = membership.UserId
            });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<SalesTeam> VisibleTeams(Actor actor)
    {
        var query = _db.SalesTeams.AsNoTracking();

        if (actor.IsAdmin)
        {
            return query;
        }

        return query.Where(team =>
            team.IsActive &&
            (
                (actor.IsSupervisor &&
                 team.SupervisorUserId == actor.UserId)
                ||
                (actor.IsSeller &&
                 _db.SalesTeamMembers.Any(member =>
                     member.SalesTeamId == team.Id &&
                     member.UserId == actor.UserId &&
                     member.LeftAtUtc == null))
            ));
    }

    private async Task<Actor> GetActorAsync(
        CancellationToken cancellationToken)
    {
        string? userId = _currentUser.UserId;

        if (!_currentUser.IsAuthenticated ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException();
        }

        bool active = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId && user.IsActive,
                cancellationToken);

        if (!active)
        {
            throw new UnauthorizedAccessException();
        }

        var roles = await (
            from userRole in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role.Name)
            .ToListAsync(cancellationToken);

        var actor = new Actor(
            userId,
            roles.Contains(AppRoles.Admin),
            roles.Contains(AppRoles.SalesSupervisor),
            roles.Contains(AppRoles.Seller));

        if (!actor.IsAdmin &&
            !actor.IsSupervisor &&
            !actor.IsSeller)
        {
            throw new UnauthorizedAccessException();
        }

        return actor;
    }

    private async Task<SalesTeam> GetManageableTeamAsync(
        Guid teamId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        var team = await _db.SalesTeams
            .SingleOrDefaultAsync(
                team =>
                    team.Id == teamId &&
                    (
                        actor.IsAdmin ||
                        (actor.IsSupervisor &&
                         team.SupervisorUserId == actor.UserId)
                    ),
                cancellationToken);

        if (team is null)
        {
            throw new KeyNotFoundException();
        }

        if (!team.IsActive)
        {
            throw new SalesTeamException(
                "El equipo está inactivo.");
        }

        return team;
    }

    private static void CheckVersion(
        SalesTeam team,
        byte[] rowVersion)
    {
        if (rowVersion is null ||
            rowVersion.Length != 8 ||
            !team.RowVersion.SequenceEqual(rowVersion))
        {
            throw new SalesTeamException(
                "El equipo cambió desde que abriste la página. " +
                "Revisa la información actual antes de repetir la acción.");
        }
    }

    private void Touch(SalesTeam team, DateTime now)
    {
        team.UpdatedAtUtc = now;

        // Forzar la actualización de la cabecera para que SQL Server
        // genere una nueva RowVersion después de cambiar miembros.
        _db.Entry(team)
            .Property(item => item.UpdatedAtUtc)
            .IsModified = true;
    }

    private static string RequiredText(
        string? value,
        int maximumLength,
        string field)
    {
        string text = value?.Trim() ?? string.Empty;

        if (text.Length == 0 || text.Length > maximumLength)
        {
            throw new SalesTeamException(
                $"{field} es obligatorio y admite " +
                $"hasta {maximumLength} caracteres.");
        }

        return text;
    }

    private void AddAudit(
        string actorUserId,
        Guid teamId,
        string action,
        object summary)
    {
        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            ActorType = "User",
            Action = action,
            EntityType = "SalesTeam",
            EntityId = teamId.ToString(),
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            SummaryJson = JsonSerializer.Serialize(summary)
        });
    }

    private async Task<IDbContextTransaction> BeginWriteAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                """
                DECLARE @result int;

                EXEC @result = sys.sp_getapplock
                    @Resource = N'PortalPedidos.SalesTeams',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 5000;

                IF @result < 0
                    THROW 51070, 'No se pudo bloquear la gestión de equipos.', 1;
                """,
                cancellationToken);

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}