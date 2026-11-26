using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Exceptions;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Queries.Common;
using Portal.Infrastructure.Queries.Customers;

namespace Portal.Infrastructure.Services.Customers;

public sealed class CustomerCoverageService(PortalDbContext db, CommercialPortfolioQuery portfolio,
    ICurrentUser actor, TimeProvider clock)
{
    public async Task GrantAsync(Guid customerId, Guid assignmentId, string email, DateOnly from, DateOnly to,
        string reason, CancellationToken ct)
    {
        var (start, end) = PeruDateRange.Bounds(from, to);
        reason = CustomerPortfolioService.NormalizeReason(reason);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        string admin = await LockAndAuthorizeAsync(customerId, ct);
        if (await portfolio.HolderAssignmentAsync(customerId, ct) != assignmentId)
            throw new CustomerPortfolioException("La asignación titular cambió o perdió vigencia. Revisa la ficha.");
        var member = await portfolio.SubstituteMemberAsync(assignmentId, email.Trim(), ct);
        if (member == null)
            throw new CustomerPortfolioException("Elige otro vendedor activo del mismo equipo, distinto del titular.");

        var now = clock.GetUtcNow().UtcDateTime;
        if (end <= now)
            throw new CustomerPortfolioException("El período de cobertura ya terminó.");
        if (await db.CustomerCoverages.AnyAsync(c => c.AssignmentId == assignmentId && c.RevokedAtUtc == null &&
                c.StartsAtUtc < end && c.EndsAtUtc > start, ct))
            throw new CustomerPortfolioException("Ya existe una suplencia que se superpone con esas fechas.");

        var coverage = new CustomerCoverage
        {
            Id = Guid.NewGuid(), AssignmentId = assignmentId, SubstituteMemberId = member.Value,
            StartsAtUtc = start!.Value, EndsAtUtc = end!.Value, GrantedAtUtc = now,
            GrantedByUserId = admin, Reason = reason
        };
        db.CustomerCoverages.Add(coverage);
        Audit(customerId, coverage, admin, "CustomerPortfolio.CoverageGranted", reason, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RevokeAsync(Guid customerId, Guid coverageId, string reason, CancellationToken ct)
    {
        reason = CustomerPortfolioService.NormalizeReason(reason);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        string admin = await LockAndAuthorizeAsync(customerId, ct);
        var coverage = await (from c in db.CustomerCoverages
            join a in db.CustomerAssignments on c.AssignmentId equals a.Id
            where c.Id == coverageId && a.CustomerId == customerId select c).SingleOrDefaultAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (coverage == null || coverage.RevokedAtUtc.HasValue || coverage.EndsAtUtc <= now)
            throw new CustomerPortfolioException("La suplencia ya no admite revocación. Revisa el historial.");
        coverage.RevokedAtUtc = now;
        coverage.RevokedByUserId = admin;
        coverage.RevokeReason = reason;
        Audit(customerId, coverage, admin, "CustomerPortfolio.CoverageRevoked", reason, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<string> LockAndAuthorizeAsync(Guid customerId, CancellationToken ct)
    {
        await PortfolioLocks.ForCustomerAsync(db, customerId, true, ct);
        if (actor.UserId is not string id || !await portfolio.ActiveUsers(AppRoles.Admin).ContainsAsync(id, ct))
            throw new UnauthorizedAccessException();
        return id;
    }

    private void Audit(Guid customerId, CustomerCoverage c, string admin, string action, string reason, DateTime now)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Action = action, ActorType = "User", ActorUserId = admin, EntityType = "Customer",
            EntityId = customerId.ToString("D"), OccurredAtUtc = now, CorrelationId = Guid.NewGuid(),
            SummaryJson = JsonSerializer.Serialize(new { customerId, coverageId = c.Id, c.AssignmentId,
                c.SubstituteMemberId, c.StartsAtUtc, c.EndsAtUtc, reason })
        });
    }
}
