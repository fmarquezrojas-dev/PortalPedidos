using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Customers;
using Portal.Application.Interfaces.Customers;
using Portal.Application.Interfaces.Security;
using Portal.Domain.Entities;
using Portal.Infrastructure.Queries.Common;

namespace Portal.Infrastructure.Queries.Customers;

public sealed class CustomerDirectoryQuery : ICustomerDirectoryQuery
{
    private const int PageSize = 20;
    private const int MaximumSearchLength = 100;

    private readonly global::Portal.Infrastructure.Persistence.PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerEligibilityService _customerEligibility;

    public CustomerDirectoryQuery(
        global::Portal.Infrastructure.Persistence.PortalDbContext db,
        ICurrentUser currentUser,
        ICustomerEligibilityService customerEligibility)
    {
        _db = db;
        _currentUser = currentUser;
        _customerEligibility = customerEligibility;
    }

    public async Task<CustomerDirectoryPageDto> SearchAsync(
        string? search,
        bool? isActive,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.CanViewOrders)
        {
            throw new UnauthorizedAccessException(
                "No tienes permisos para consultar clientes.");
        }

        string? normalizedSearch = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();

        if (normalizedSearch is { Length: > MaximumSearchLength })
        {
            throw new ArgumentException(
                "La búsqueda no puede superar los 100 caracteres.",
                nameof(search));
        }

        IQueryable<Customer> query = _db.Customers
            .AsNoTracking();

        if (normalizedSearch is not null)
        {
            query = query.Matching(normalizedSearch);
        }

        // El directorio permite consultar clientes activos e inactivos.
        // La elegibilidad para pedidos no modifica este filtro.
        if (isActive.HasValue)
        {
            bool requiredStatus = isActive.Value;

            query = query.Where(customer =>
                customer.IsActive == requiredStatus);
        }

        int totalCount = await query.CountAsync(
            cancellationToken);

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)PageSize));

        int currentPage = Math.Clamp(
            pageNumber,
            1,
            totalPages);

        // Filtrar, ordenar y paginar en la base de datos.
        // Solo se recuperan los clientes de la página solicitada.
        var eligibleIds = _db.Customers.Where(_customerEligibility.GetEligibleCustomerPredicate()).Select(c => c.Id);
        var customers = await query
            .OrderBy(customer => customer.ExternalCode)
            .ThenBy(customer => customer.Id)
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(customer => new { Customer = customer, Eligible = eligibleIds.Contains(customer.Id) })
            .ToListAsync(cancellationToken);

        // La elegibilidad ya viene en la proyección SQL de esta página.
        var items = customers
            .Select(row =>
            {
                var customer = row.Customer;
                var eligibility = row.Eligible ? CustomerEligibilityResultDto.Eligible() :
                    customer.IsActive ? CustomerEligibilityResultDto.OutsidePortfolio() : CustomerEligibilityResultDto.Inactive();

                return new CustomerDirectoryItemDto
                {
                    Id = customer.Id,
                    ExternalCode = customer.ExternalCode,
                    Name = customer.Name,
                    IsActive = customer.IsActive,
                    UpdatedAtUtc = customer.UpdatedAtUtc,

                    IsEligibleForOrders = eligibility.IsEligible,
                    EligibilityCode = eligibility.Code,
                    EligibilityMessage = eligibility.Message
                };
            })
            .ToList();

        var catalogState = await _db.CatalogSyncStates
            .AsNoTracking()
            .Where(state => state.Id == 1)
            .Select(state => new
            {
                state.CurrentVersion,
                state.LastSuccessAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        DateTime? lastSuccessAtUtc = null;

        if (catalogState?.LastSuccessAtUtc is DateTime lastSuccess)
        {
            lastSuccessAtUtc = DateTime.SpecifyKind(
                lastSuccess,
                DateTimeKind.Utc);
        }

        DateTime now = DateTime.UtcNow;

        bool catalogIsFresh =
            catalogState is not null &&
            catalogState.CurrentVersion > 0 &&
            lastSuccessAtUtc.HasValue &&
            lastSuccessAtUtc.Value >= now.AddMinutes(-5) &&
            lastSuccessAtUtc.Value <= now.AddMinutes(1);

        return new CustomerDirectoryPageDto
        {
            Search = normalizedSearch,
            IsActive = isActive,

            TotalCount = totalCount,
            PageNumber = currentPage,
            PageSize = PageSize,

            CatalogVersion = catalogState?.CurrentVersion ?? 0,
            LastSuccessAtUtc = lastSuccessAtUtc,
            CatalogIsFresh = catalogIsFresh,

            Items = items
        };
    }
}
