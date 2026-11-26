using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Customers;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Queries.Common;

namespace Portal.Infrastructure.Queries.Customers;

public sealed class CommercialPortfolioQuery(PortalDbContext db, ICurrentUser actor, TimeProvider clock)
{
    internal IQueryable<string> ActiveUsers(string roleName) =>
        from user in db.Users
        join link in db.UserRoles on user.Id equals link.UserId
        join role in db.Roles on link.RoleId equals role.Id
        where user.IsActive && role.Name == roleName
        select user.Id;

    private IQueryable<Member> Members() =>
        from m in db.SalesTeamMembers
        join u in db.Users on m.UserId equals u.Id
        join t in db.SalesTeams on m.SalesTeamId equals t.Id
        select new Member
        {
            Id = m.Id, UserId = u.Id, Email = u.Email ?? u.UserName!, TeamId = t.Id,
            Team = t.Name, SupervisorId = t.SupervisorUserId,
            Problem = m.LeftAtUtc != null ? "Membresía finalizada" : !t.IsActive ? "Equipo inactivo" :
                !u.IsActive ? "Cuenta inactiva" : !ActiveUsers(AppRoles.Seller).Contains(u.Id) ? "Sin rol vendedor" : null
        };

    private IQueryable<Assignment> Assignments() =>
        from a in db.CustomerAssignments
        join c in db.Customers on a.CustomerId equals c.Id
        join m in Members() on a.SalesTeamMemberId equals m.Id
        select new Assignment
        {
            Id = a.Id, CustomerId = c.Id, Code = c.ExternalCode, Name = c.Name, Holder = m,
            Problem = a.EndedAtUtc != null ? "Asignación cerrada: " + a.EndReason :
                !c.IsActive ? "Cliente inactivo" : m.Problem
        };

    public IQueryable<CoverageEntry> CoverageHistory() =>
        from coverage in db.CustomerCoverages
        join a in Assignments() on coverage.AssignmentId equals a.Id
        join sub in Members() on coverage.SubstituteMemberId equals sub.Id
        join author in db.Users on coverage.GrantedByUserId equals author.Id
        join revoker in db.Users on coverage.RevokedByUserId equals revoker.Id into revokers
        from revoker in revokers.DefaultIfEmpty()
        select new CoverageEntry
        {
            Id = coverage.Id, AssignmentId = a.Id, CustomerId = a.CustomerId,
            Holder = a.Holder.Email, SubstituteId = sub.UserId, Substitute = sub.Email,
            GrantedBy = author.Email ?? author.UserName!, Reason = coverage.Reason,
            GrantedAtUtc = coverage.GrantedAtUtc,
            StartsAtUtc = coverage.StartsAtUtc, EndsAtUtc = coverage.EndsAtUtc,
            RevokedAtUtc = coverage.RevokedAtUtc, RevokeReason = coverage.RevokeReason,
            RevokedBy = revoker == null ? null : revoker.Email,
            Problem = a.Problem != null ? "Titular: " + a.Problem :
                sub.TeamId != a.Holder.TeamId ? "Equipos distintos" :
                sub.Problem != null ? "Suplente: " + sub.Problem : null
        };

    // Una fila por cliente, incluso cuando el titular tiene una suplencia vigente.
    internal IQueryable<CommercialAccount> Accounts(DateTime now)
    {
        var current = CoverageHistory().Where(c => c.RevokedAtUtc == null && c.Problem == null &&
            c.StartsAtUtc <= now && now < c.EndsAtUtc);
        return from a in Assignments()
               where a.Problem == null
               join c in current on a.Id equals c.AssignmentId into covers
               from c in covers.DefaultIfEmpty()
               select new CommercialAccount
               {
                   CustomerId = a.CustomerId, AssignmentId = a.Id, Code = a.Code, Name = a.Name,
                   HolderId = a.Holder.UserId, Holder = a.Holder.Email, Team = a.Holder.Team,
                   SupervisorId = a.Holder.SupervisorId, SubstituteId = c.SubstituteId,
                   Substitute = c.Substitute, EndsAtUtc = (DateTime?)c.EndsAtUtc
               };
    }

    internal Task<Guid?> HolderAssignmentAsync(Guid customerId, CancellationToken ct) =>
        Assignments().Where(a => a.CustomerId == customerId && a.Problem == null)
            .Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);

    internal Task<Guid?> SubstituteMemberAsync(Guid assignmentId, string email, CancellationToken ct) =>
        (from a in Assignments()
         from m in Members()
         where a.Id == assignmentId && a.Problem == null && m.Problem == null &&
             m.TeamId == a.Holder.TeamId && m.UserId != a.Holder.UserId && m.Email == email
         select (Guid?)m.Id).SingleOrDefaultAsync(ct);

    private IQueryable<CommercialAccount> Scoped(bool team, DateTime now)
    {
        string userId = actor.UserId ?? throw new UnauthorizedAccessException();
        var rows = Accounts(now);
        return team
            ? rows.Where(r => r.SupervisorId == userId && ActiveUsers(AppRoles.SalesSupervisor).Contains(userId))
            : rows.Where(r => r.HolderId == userId || r.SubstituteId == userId);
    }

    public Task<CommercialAccount?> FindAsync(Guid id, bool team, CancellationToken ct) =>
        Scoped(team, clock.GetUtcNow().UtcDateTime).SingleOrDefaultAsync(r => r.CustomerId == id, ct);

    public async Task<CommercialPortfolioPage> SearchAsync(bool team, string? search, PortfolioFilter? filter,
        int page, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var until = PeruDateRange.Bounds(null, PeruDateRange.LocalDate(now).AddDays(6)).UntilUtc!.Value;
        var rows = Scoped(team, now);
        var userId = actor.UserId!;
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            rows = rows.Where(r => r.Code.Contains(search) || r.Name.Contains(search));

        var counts = await rows.GroupBy(r => 1).Select(g => new
        {
            Total = g.Count(),
            Holders = g.Count(r => team || r.HolderId == userId),
            Substitutes = g.Count(r => team ? r.SubstituteId != null : r.SubstituteId == userId),
            Expiring = g.Count(r => r.EndsAtUtc <= until && (team || r.SubstituteId == userId))
        }).SingleOrDefaultAsync(ct);

        int total = counts?.Total ?? 0;
        switch (filter)
        {
            case PortfolioFilter.Holder:
                rows = rows.Where(r => team || r.HolderId == userId);
                total = counts?.Holders ?? 0;
                break;
            case PortfolioFilter.Substitute:
                rows = rows.Where(r => team ? r.SubstituteId != null : r.SubstituteId == userId);
                total = counts?.Substitutes ?? 0;
                break;
            case PortfolioFilter.Expiring:
                rows = rows.Where(r => r.EndsAtUtc <= until && (team || r.SubstituteId == userId));
                total = counts?.Expiring ?? 0;
                break;
        }
        int pages = Math.Max(1, (int)Math.Ceiling(total / 20d));
        page = Math.Clamp(page, 1, pages);
        var items = await rows.OrderBy(r => r.Code).ThenBy(r => r.CustomerId)
            .Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new(items, userId, team, search, filter, total, page, pages,
            counts?.Holders ?? 0, counts?.Substitutes ?? 0, counts?.Expiring ?? 0, until);
    }

    private sealed class Member
    {
        public Guid Id { get; init; }
        public Guid TeamId { get; init; }
        public string UserId { get; init; } = "";
        public string Email { get; init; } = "";
        public string Team { get; init; } = "";
        public string SupervisorId { get; init; } = "";
        public string? Problem { get; init; }
    }

    private sealed class Assignment
    {
        public Guid Id { get; init; }
        public Guid CustomerId { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public Member Holder { get; init; } = null!;
        public string? Problem { get; init; }
    }
}
