using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Portal.Infrastructure.Queries.Common;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Audit;
using Portal.Application.Interfaces.Audit;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Queries.Audit;

public class AuditQuery : IAuditQuery
{
    private const int PageSize = 20;

    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuditQuery(
        PortalDbContext db,
        ICurrentUser currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    public Task<AuditPageDto> SearchAsync(string? search, int pageNumber,
        CancellationToken cancellationToken = default)
        => SearchAsync(new AuditSearchDto { Search = search, PageNumber = pageNumber }, cancellationToken);

    public async Task<AuditPageDto> SearchAsync(AuditSearchDto filters,
        CancellationToken cancellationToken = default)
    {
        await RequireOperatorAsync();
        ArgumentNullException.ThrowIfNull(filters);
        string? search = string.IsNullOrWhiteSpace(filters.Search) ? null : filters.Search.Trim();
        string? module = string.IsNullOrWhiteSpace(filters.Module) ? null : filters.Module.Trim();
        string? action = string.IsNullOrWhiteSpace(filters.EventCode) ? null : filters.EventCode.Trim();
        string? actorType = string.IsNullOrWhiteSpace(filters.ActorType) ? null : filters.ActorType.Trim();
        if (search?.Length > 100 || action?.Length > 80)
            throw new ArgumentException("El texto de búsqueda o acción es demasiado largo.");
        if (module is not null && module is not ("Order" or "Integration" or "CustomerPortfolio" or "SalesTeam" or "User"))
            throw new ArgumentException("El módulo seleccionado no es válido.");
        if (actorType is not null && actorType is not ("User" or "System"))
            throw new ArgumentException("El tipo de responsable no es válido.");
        var range = PeruDateRange.Bounds(filters.From, filters.To);
        var events = _db.AuditEvents.AsNoTracking();
        if (range.FromUtc.HasValue) events = events.Where(item => item.OccurredAtUtc >= range.FromUtc.Value);
        if (range.UntilUtc.HasValue) events = events.Where(item => item.OccurredAtUtc < range.UntilUtc.Value);
        if (module is not null) events = events.Where(item => item.Action.StartsWith(module + "."));
        if (action is not null) events = events.Where(item => item.Action == action);
        if (actorType is not null) events = events.Where(item => item.ActorType == actorType);
        if (filters.CorrelationId.HasValue) events = events.Where(item => item.CorrelationId == filters.CorrelationId.Value);
        int pageNumber = filters.PageNumber;

        var query =
            from audit in events
            join user in _db.Users.AsNoTracking()
                on audit.ActorUserId equals user.Id
                into users
            from user in users.DefaultIfEmpty()
            select new AuditListItemDto
            {
                Id = audit.Id,
                OccurredAtUtc = audit.OccurredAtUtc,
                ActorUserId = audit.ActorUserId,
                ActorEmail = user == null ? null : user.Email,
                ActorType = audit.ActorType,
                Action = audit.Action,
                EntityType = audit.EntityType,
                EntityId = audit.EntityId
            };

        if (search is not null)
        {
            query = query.Where(item =>
                item.Action.Contains(search) ||
                item.EntityType.Contains(search) ||
                item.EntityId.Contains(search) ||
                (item.ActorEmail != null &&
                 item.ActorEmail.Contains(search)));
        }

        int totalCount = await query.CountAsync(
            cancellationToken);

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)PageSize));

        pageNumber = Math.Clamp(
            pageNumber,
            1,
            totalPages);

        var items = await query
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        return new AuditPageDto
        {
            Search = search,
            Filters = new AuditSearchDto { Search = search, Module = module, EventCode = action,
                ActorType = actorType, From = filters.From, To = filters.To,
                CorrelationId = filters.CorrelationId, PageNumber = pageNumber },
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = PageSize
        };
    }

    public async Task<AuditDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await RequireOperatorAsync();

        return await (
            from audit in _db.AuditEvents.AsNoTracking()
            join user in _db.Users.AsNoTracking()
                on audit.ActorUserId equals user.Id
                into users
            from user in users.DefaultIfEmpty()
            where audit.Id == id
            select new AuditDetailDto
            {
                Id = audit.Id,
                OccurredAtUtc = audit.OccurredAtUtc,
                ActorUserId = audit.ActorUserId,
                ActorEmail = user == null ? null : user.Email,
                ActorType = audit.ActorType,
                Action = audit.Action,
                EntityType = audit.EntityType,
                EntityId = audit.EntityId,
                CorrelationId = audit.CorrelationId,
                SummaryJson = audit.SummaryJson
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task RequireOperatorAsync()
    {
        string? userId = _currentUser.UserId;

        if (!_currentUser.IsAuthenticated ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException(
                "Debes iniciar sesión.");
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user is null ||
            !user.IsActive ||
            !((await _userManager.IsInRoleAsync(user, AppRoles.Admin)) ||
              (await _userManager.IsInRoleAsync(user, AppRoles.Operator))))
        {
            throw new UnauthorizedAccessException(
                "Solo un administrador u operador activo puede consultar la auditoría.");
        }
    }
}
