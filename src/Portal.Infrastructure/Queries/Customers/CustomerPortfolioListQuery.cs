using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Customers;
using Portal.Application.Interfaces.Customers;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Queries.Customers;

public sealed class CustomerPortfolioListQuery : ICustomerPortfolioListQuery
{
    private const int PageSize = 20;
    private const int MaximumSearchLength = 100;
    private const int MaximumSellerSearchLength = 256;
    private const int MaximumTeamSearchLength = 100;

    private readonly global::Portal.Infrastructure.Persistence.PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerEligibilityService _customerEligibility;

    public CustomerPortfolioListQuery(
        global::Portal.Infrastructure.Persistence.PortalDbContext db,
        ICurrentUser currentUser,
        ICustomerEligibilityService customerEligibility)
    {
        _db = db;
        _currentUser = currentUser;
        _customerEligibility = customerEligibility;
    }

    public async Task<CustomerPortfolioListPageDto> SearchAsync(
        CustomerPortfolioListFilterDto filters,
        CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync(cancellationToken);

        if (filters is null)
        {
            throw new ArgumentNullException(nameof(filters));
        }

        string? search = NormalizeSearch(
            filters.Search,
            MaximumSearchLength,
            "La búsqueda de clientes",
            nameof(filters.Search));

        string? sellerSearch = NormalizeSearch(
            filters.SellerSearch,
            MaximumSellerSearchLength,
            "La búsqueda de responsables",
            nameof(filters.SellerSearch));

        string? teamSearch = NormalizeSearch(
            filters.TeamSearch,
            MaximumTeamSearchLength,
            "La búsqueda de equipos",
            nameof(filters.TeamSearch));

        if (filters.AssignmentStatus.HasValue &&
            !Enum.IsDefined(
                typeof(CustomerPortfolioAssignmentStatus),
                filters.AssignmentStatus.Value))
        {
            throw new ArgumentException(
                "La situación de la asignación no es válida.",
                nameof(filters.AssignmentStatus));
        }

        // Solo participan las asignaciones abiertas.
        // El historial continúa consultándose desde la ficha.
        var openAssignments = _db.CustomerAssignments
            .AsNoTracking()
            .Where(assignment => assignment.EndedAtUtc == null);

        // LEFT JOIN para conservar clientes sin responsable.
        // También permite detectar referencias no disponibles,
        // sin hacer desaparecer al cliente del listado.
        var query =
            from customer in _db.Customers.AsNoTracking()

            join assignment in openAssignments
                on customer.Id equals assignment.CustomerId
                into customerAssignments
            from assignment in customerAssignments.DefaultIfEmpty()

            join membership in _db.SalesTeamMembers.AsNoTracking()
                on assignment.SalesTeamMemberId equals membership.Id
                into assignmentMemberships
            from membership in assignmentMemberships.DefaultIfEmpty()

            join team in _db.SalesTeams.AsNoTracking()
                on membership.SalesTeamId equals team.Id
                into membershipTeams
            from team in membershipTeams.DefaultIfEmpty()

            join seller in _db.Users.AsNoTracking()
                on membership.UserId equals seller.Id
                into membershipSellers
            from seller in membershipSellers.DefaultIfEmpty()

            select new
            {
                Customer = customer,

                HasAssignment = assignment != null,

                MembershipExists = membership != null,
                TeamExists = team != null,
                SellerExists = seller != null,

                SellerEmail = seller == null
                    ? null
                    : seller.Email ?? seller.UserName,

                TeamCode = team == null ? null : team.Code,
                TeamName = team == null ? null : team.Name,

                SellerIsActive =
                    seller != null &&
                    seller.IsActive,

                MembershipIsActive =
                    membership != null &&
                    membership.LeftAtUtc == null,

                TeamIsActive =
                    team != null &&
                    team.IsActive,

                SellerHasRole =
                    seller != null &&
                    (
                        from userRole in _db.UserRoles
                        join role in _db.Roles
                            on userRole.RoleId equals role.Id
                        where userRole.UserId == seller.Id &&
                              role.Name == AppRoles.Seller
                        select userRole.UserId
                    ).Any(),

                AssignedAtUtc = assignment == null
                    ? (DateTime?)null
                    : assignment.AssignedAtUtc
            };

        if (search is not null)
        {
            query = query.Where(item =>
                item.Customer.ExternalCode.Contains(search) ||
                item.Customer.Name.Contains(search));
        }

        if (sellerSearch is not null)
        {
            query = query.Where(item =>
                item.SellerEmail != null &&
                item.SellerEmail.Contains(sellerSearch));
        }

        if (teamSearch is not null)
        {
            query = query.Where(item =>
                (item.TeamCode != null &&
                 item.TeamCode.Contains(teamSearch)) ||
                (item.TeamName != null &&
                 item.TeamName.Contains(teamSearch)));
        }

        if (filters.CustomerIsActive.HasValue)
        {
            bool requiredStatus = filters.CustomerIsActive.Value;

            query = query.Where(item =>
                item.Customer.IsActive == requiredStatus);
        }

        // Estos filtros se aplican ANTES del conteo y la paginación.
        switch (filters.AssignmentStatus)
        {
            case CustomerPortfolioAssignmentStatus.Unassigned:
                query = query.Where(item => !item.HasAssignment);
                break;

            case CustomerPortfolioAssignmentStatus.Operational:
                query = query.Where(item =>
                    item.HasAssignment &&
                    item.SellerIsActive &&
                    item.SellerHasRole &&
                    item.MembershipIsActive &&
                    item.TeamIsActive);
                break;

            case CustomerPortfolioAssignmentStatus.RequiresReview:
                query = query.Where(item =>
                    item.HasAssignment &&
                    (
                        !item.SellerIsActive ||
                        !item.SellerHasRole ||
                        !item.MembershipIsActive ||
                        !item.TeamIsActive
                    ));
                break;
        }

        int totalCount = await query.CountAsync(cancellationToken);

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)PageSize));

        int currentPage = Math.Clamp(
            filters.PageNumber,
            1,
            totalPages);

        var rows = await query
            .OrderBy(item => item.Customer.ExternalCode)
            .ThenBy(item => item.Customer.Id)
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        var items = new List<CustomerPortfolioListItemDto>(rows.Count);

        foreach (var row in rows)
        {
            // La elegibilidad se evalúa sobre los clientes materializados.
            // No se introduce Evaluate dentro de una consulta SQL.
            var eligibility = _customerEligibility.Evaluate(row.Customer);

            var reviewReasons = new List<string>();

            CustomerPortfolioAssignmentStatus assignmentStatus;

            if (!row.HasAssignment)
            {
                assignmentStatus =
                    CustomerPortfolioAssignmentStatus.Unassigned;
            }
            else
            {
                if (!row.MembershipExists)
                {
                    reviewReasons.Add(
                        "La membresía asociada a la asignación no está disponible.");
                }
                else
                {
                    if (!row.MembershipIsActive)
                    {
                        reviewReasons.Add(
                            "El vendedor ya no pertenece a esta membresía del equipo.");
                    }

                    if (!row.SellerExists)
                    {
                        reviewReasons.Add(
                            "La cuenta del vendedor no está disponible.");
                    }
                    else
                    {
                        if (!row.SellerIsActive)
                        {
                            reviewReasons.Add(
                                "La cuenta del vendedor está inactiva.");
                        }

                        if (!row.SellerHasRole)
                        {
                            reviewReasons.Add(
                                "La cuenta ya no tiene el rol Vendedor.");
                        }
                    }

                    if (!row.TeamExists)
                    {
                        reviewReasons.Add(
                            "El equipo comercial no está disponible.");
                    }
                    else if (!row.TeamIsActive)
                    {
                        reviewReasons.Add(
                            "El equipo comercial está inactivo.");
                    }
                }

                assignmentStatus = reviewReasons.Count == 0
                    ? CustomerPortfolioAssignmentStatus.Operational
                    : CustomerPortfolioAssignmentStatus.RequiresReview;
            }

            items.Add(new CustomerPortfolioListItemDto
            {
                CustomerId = row.Customer.Id,
                CustomerCode = row.Customer.ExternalCode,
                CustomerName = row.Customer.Name,
                CustomerIsActive = row.Customer.IsActive,

                IsEligibleForOrders = eligibility.IsEligible,
                EligibilityCode = eligibility.Code,
                EligibilityMessage = eligibility.Message,

                AssignmentStatus = assignmentStatus,

                SellerEmail = row.SellerEmail,
                TeamCode = row.TeamCode,
                TeamName = row.TeamName,
                AssignedAtUtc = row.AssignedAtUtc,

                ReviewReasons = reviewReasons
            });
        }

        return new CustomerPortfolioListPageDto
        {
            Filters = new CustomerPortfolioListFilterDto
            {
                Search = search,
                SellerSearch = sellerSearch,
                TeamSearch = teamSearch,
                CustomerIsActive = filters.CustomerIsActive,
                AssignmentStatus = filters.AssignmentStatus,
                PageNumber = currentPage
            },

            TotalCount = totalCount,
            PageNumber = currentPage,
            PageSize = PageSize,
            Items = items
        };
    }

    private static string? NormalizeSearch(
        string? value,
        int maximumLength,
        string label,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{label} no puede superar los {maximumLength} caracteres.",
                parameterName);
        }

        return normalized;
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