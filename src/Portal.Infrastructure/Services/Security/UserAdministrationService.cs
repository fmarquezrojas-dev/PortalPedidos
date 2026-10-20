using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Security;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
namespace Portal.Infrastructure.Services.Security;

public sealed class UserAdministrationService : IUserAdministrationService
{
    private readonly PortalDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ICurrentUser _currentUser;
    // Solo roles con funciones habilitadas. No habilitar Gerencia/Auditor por registrarlos.
    private static readonly string[] Assignable =
        { AppRoles.Admin, AppRoles.Seller, AppRoles.Operator, AppRoles.SalesSupervisor };

    public UserAdministrationService(PortalDbContext db, UserManager<ApplicationUser> users, ICurrentUser currentUser)
    { _db = db; _users = users; _currentUser = currentUser; }

    private async Task<string> RequireAdminAsync(CancellationToken ct)
    {
        string? id = _currentUser.UserId;
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(id))
            throw new UnauthorizedAccessException();
        bool allowed = await (from u in _db.Users.AsNoTracking()
            join ur in _db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where u.Id == id && u.IsActive && r.Name == AppRoles.Admin select u.Id).AnyAsync(ct);
        if (!allowed) throw new UnauthorizedAccessException();
        return id;
    }

    public async Task<ManagedUserPageDto> SearchAsync(string? search, bool? isActive, int pageNumber, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(cancellationToken);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 100) throw new UserAdministrationException("La búsqueda no puede superar 100 caracteres.");
        var query = _db.Users.AsNoTracking();
        if (search is not null) query = query.Where(u => u.Email != null && u.Email.Contains(search));
        if (isActive.HasValue) query = query.Where(u => u.IsActive == isActive.Value);
        int count = await query.CountAsync(cancellationToken);
        int page = Math.Clamp(pageNumber, 1, Math.Max(1, (int)Math.Ceiling(count / 20d)));
        var items = await query.OrderBy(u => u.Email).ThenBy(u => u.Id).Skip((page - 1) * 20).Take(20)
            .Select(u => new ManagedUserItemDto { Id = u.Id, Email = u.Email ?? "", IsActive = u.IsActive, CreatedAtUtc = u.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var ids = items.Select(u => u.Id).ToArray();
        var assignments = await (from ur in _db.UserRoles.AsNoTracking()
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId) select new { ur.UserId, r.Name }).ToListAsync(cancellationToken);
        foreach (var item in items) item.Roles = assignments.Where(r => r.UserId == item.Id && r.Name != null)
            .Select(r => r.Name!).OrderBy(r => r).ToArray();
        return new ManagedUserPageDto { Search = search, IsActive = isActive, PageNumber = page, TotalCount = count, Items = items };
    }

    public async Task<IReadOnlyList<string>> GetAssignableRolesAsync(CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(cancellationToken);
        return await _db.Roles.AsNoTracking().Where(r => r.Name != null && Assignable.Contains(r.Name))
            .OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync(cancellationToken);
    }

    public async Task<ManagedUserEditDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(cancellationToken);
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return null;
        var roles = await (from ur in _db.UserRoles.AsNoTracking()
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == id && r.Name != null select r.Name!).ToListAsync(cancellationToken);
        return new ManagedUserEditDto { Id = id, Email = user.Email ?? "", IsActive = user.IsActive,
            ConcurrencyStamp = user.ConcurrencyStamp ?? "", Roles = roles };
    }

    public async Task<string> CreateAsync(CreateManagedUserRequest request, CancellationToken cancellationToken = default)
    {
        string email = request.Email?.Trim() ?? "";
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            throw new UserAdministrationException("Indica un correo válido de hasta 256 caracteres.");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            throw new UserAdministrationException("Indica una contraseña válida de hasta 128 caracteres.");
        string reason = ValidateReason(request.Reason);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(cancellationToken);
        string actor = await RequireAdminAsync(cancellationToken);
        var roles = await ValidateRolesAsync(request.Roles, cancellationToken);
        if (await _users.FindByEmailAsync(email) is not null)
            throw new UserAdministrationException("El correo ya está registrado.");
        var user = new ApplicationUser { Email = email, UserName = email, IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow };
        Ensure(await _users.CreateAsync(user, request.Password));
        Ensure(await _users.AddToRolesAsync(user, roles));
        AddAudit(actor, user.Id, "User.Created", new { reason, user.IsActive, roles });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }

    public async Task<bool> UpdateAsync(UpdateManagedUserRequest request, CancellationToken cancellationToken = default)
    {
        string reason = ValidateReason(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.ExpectedConcurrencyStamp))
            throw new UserAdministrationException("La versión de la cuenta es inválida. Recarga la ficha.", true);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(cancellationToken);
        string actor = await RequireAdminAsync(cancellationToken);
        var user = await _users.FindByIdAsync(request.Id);
        if (user is null) return false;
        if (!string.Equals(user.ConcurrencyStamp, request.ExpectedConcurrencyStamp, StringComparison.Ordinal))
            throw new UserAdministrationException("La cuenta cambió desde que abriste la ficha. Recarga antes de guardar.", true);
        var selected = await ValidateRolesAsync(request.Roles, cancellationToken);
        var before = (await _users.GetRolesAsync(user)).ToArray();
        if (before.Any(r => !Assignable.Contains(r, StringComparer.Ordinal)))
            throw new UserAdministrationException("La cuenta tiene roles fuera del catálogo operativo. Requiere revisión técnica antes de editar.");
        bool wasAdmin = before.Contains(AppRoles.Admin, StringComparer.Ordinal);
        bool remainsAdmin = request.IsActive && selected.Contains(AppRoles.Admin, StringComparer.Ordinal);
        if (actor == user.Id && (!request.IsActive || !selected.Contains(AppRoles.Admin, StringComparer.Ordinal)))
            throw new UserAdministrationException("No puedes desactivar tu cuenta ni retirar tu propio rol Administrador.");
        if (user.IsActive && wasAdmin && !remainsAdmin)
        {
            bool otherAdmin = await (from u in _db.Users.AsNoTracking()
                join ur in _db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
                join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where u.Id != user.Id && u.IsActive && r.Name == AppRoles.Admin select u.Id).AnyAsync(cancellationToken);
            if (!otherAdmin) throw new UserAdministrationException("Debe permanecer al menos un administrador activo.");
        }
        bool previousActive = user.IsActive;
        var remove = before.Except(selected, StringComparer.Ordinal).ToArray();
        var add = selected.Except(before, StringComparer.Ordinal).ToArray();
        if (previousActive == request.IsActive && remove.Length == 0 && add.Length == 0)
        { await transaction.CommitAsync(cancellationToken); return true; }
        if (remove.Length > 0) Ensure(await _users.RemoveFromRolesAsync(user, remove));
        if (add.Length > 0) Ensure(await _users.AddToRolesAsync(user, add));
        user.IsActive = request.IsActive;
        // Actualiza también el ConcurrencyStamp mediante el almacén de Identity.
        Ensure(await _users.UpdateAsync(user));
        // Invalidar cookies con roles anteriores y evitar revivir sesiones tras una reactivación.
        Ensure(await _users.UpdateSecurityStampAsync(user));
        AddAudit(actor, user.Id, "User.AccessUpdated", new { reason, previousActive,
            isActive = user.IsActive, previousRoles = before, roles = selected });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<string[]> ValidateRolesAsync(IReadOnlyList<string>? roles, CancellationToken ct)
    {
        if (roles is null || roles.Count is < 1 or > 4 || roles.Any(string.IsNullOrWhiteSpace))
            throw new UserAdministrationException("Selecciona entre uno y cuatro roles operativos.");
        var selected = roles.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Any(r => !Assignable.Contains(r, StringComparer.Ordinal)))
            throw new UserAdministrationException("Se recibió un rol no permitido.");
        int registered = await _db.Roles.CountAsync(r => r.Name != null && selected.Contains(r.Name), ct);
        if (registered != selected.Length)
            throw new UserAdministrationException("Faltan roles registrados. Ejecuta la preparación de roles antes de asignarlos.");
        return selected;
    }
    private static string ValidateReason(string? value)
    {
        string reason = value?.Trim() ?? "";
        if (reason.Length is < 5 or > 300) throw new UserAdministrationException("El motivo debe tener entre 5 y 300 caracteres.");
        return reason;
    }
    private static void Ensure(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code == "ConcurrencyFailure"))
            throw new UserAdministrationException("Otra operación modificó la cuenta. Recarga la ficha.", true);
        // No guardar ni mostrar valores aportados por validadores externos.
        if (result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
            throw new UserAdministrationException("La contraseña debe cumplir la longitud y complejidad configuradas en el portal.");
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            throw new UserAdministrationException("El correo ya está registrado.");
        throw new UserAdministrationException("No se pudo guardar la cuenta. Revisa el correo, los roles y la configuración de Identity.");
    }
    private Task LockAsync(CancellationToken ct) => _db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource=N'PortalPedidos.UserAdministration',
            @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=5000;
        IF @result < 0 THROW 51040, 'No se pudo bloquear la administración de usuarios.', 1;
        """, ct);
    private void AddAudit(string actor, string id, string action, object summary)
        => _db.AuditEvents.Add(new AuditEvent { ActorUserId = actor, ActorType = "User", Action = action,
            EntityType = "ApplicationUser", EntityId = id, OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(), SummaryJson = JsonSerializer.Serialize(summary) });
}
