using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Customers;
using Portal.Application.Interfaces.Customers;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Queries.Customers;

public sealed class CustomerPortfolioQuery : ICustomerPortfolioQuery
{
    private const int HistoryLimit = 20;

    private readonly PortalDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CustomerPortfolioQuery(
        PortalDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CustomerPortfolioPageDto?> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync(cancellationToken);

        if (customerId == Guid.Empty)
        {
            return null;
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .Where(item => item.Id == customerId)
            .Select(item => new
            {
                item.Id,
                item.ExternalCode,
                item.Name,
                item.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var assignments =
            from assignment in _db.CustomerAssignments.AsNoTracking()
            join membership in _db.SalesTeamMembers.AsNoTracking()
                on assignment.SalesTeamMemberId equals membership.Id
            join team in _db.SalesTeams.AsNoTracking()
                on membership.SalesTeamId equals team.Id
            join seller in _db.Users.AsNoTracking()
                on membership.UserId equals seller.Id
            where assignment.CustomerId == customerId
            select new CustomerPortfolioAssignmentDto
            {
                Id = assignment.Id,

                SellerEmail = seller.Email ?? seller.UserName ?? "",
                TeamCode = team.Code,
                TeamName = team.Name,

                SellerIsActive = seller.IsActive,
                MembershipIsActive = membership.LeftAtUtc == null,
                TeamIsActive = team.IsActive,

                SellerHasRole = (
                    from userRole in _db.UserRoles
                    join role in _db.Roles
                        on userRole.RoleId equals role.Id
                    where userRole.UserId == seller.Id &&
                          role.Name == AppRoles.Seller
                    select userRole.UserId
                ).Any(),

                AssignedAtUtc = assignment.AssignedAtUtc,

                AssignedByEmail = _db.Users
                    .Where(user =>
                        user.Id == assignment.AssignedByUserId)
                    .Select(user => user.Email ?? user.UserName)
                    .FirstOrDefault() ?? "Usuario no disponible",

                AssignmentReason = assignment.AssignmentReason,

                EndedAtUtc = assignment.EndedAtUtc,

                EndedByEmail = _db.Users
                    .Where(user =>
                        user.Id == assignment.EndedByUserId)
                    .Select(user => user.Email ?? user.UserName)
                    .FirstOrDefault(),

                EndReason = assignment.EndReason,

                RowVersion = assignment.RowVersion
            };

        var currentAssignment = await assignments
            .SingleOrDefaultAsync(
                item => item.EndedAtUtc == null,
                cancellationToken);

        int closedAssignmentCount = await assignments
            .CountAsync(
                item => item.EndedAtUtc != null,
                cancellationToken);

        var history = await assignments
            .Where(item => item.EndedAtUtc != null)
            .OrderByDescending(item => item.EndedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken);

        return new CustomerPortfolioPageDto
        {
            CustomerId = customer.Id,
            CustomerCode = customer.ExternalCode,
            CustomerName = customer.Name,
            CustomerIsActive = customer.IsActive,

            CurrentAssignment = currentAssignment,
            ClosedAssignmentCount = closedAssignmentCount,
            History = history
        };
    }

    private async Task RequireAdministratorAsync(
        CancellationToken cancellationToken)
    {
        string? userId = _currentUser.UserId;

        if (!_currentUser.IsAuthenticated ||
            string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException(
                "Debes iniciar sesión para consultar la cartera.");
        }

        bool authorized = await (
            from user in _db.Users.AsNoTracking()
            join userRole in _db.UserRoles.AsNoTracking()
                on user.Id equals userRole.UserId
            join role in _db.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where user.Id == userId &&
                  user.IsActive &&
                  role.Name == AppRoles.Admin
            select user.Id
        ).AnyAsync(cancellationToken);

        if (!authorized)
        {
            throw new UnauthorizedAccessException(
                "No tienes permisos para administrar la cartera.");
        }
    }
}