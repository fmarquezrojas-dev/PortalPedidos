using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Portal.Application.DTOs.Customers;
using Portal.Application.Interfaces.Customers;
using Portal.Application.Interfaces.Security;

namespace Portal.Infrastructure.Queries.Customers;

public sealed class CustomerEligibilityQuery
    : ICustomerEligibilityQuery
{
    private readonly global::Portal.Infrastructure.Persistence.PortalDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerEligibilityService _customerEligibility;

    public CustomerEligibilityQuery(
        global::Portal.Infrastructure.Persistence.PortalDbContext db,
        ICurrentUser currentUser,
        ICustomerEligibilityService customerEligibility)
    {
        _db = db;
        _currentUser = currentUser;
        _customerEligibility = customerEligibility;
    }

    public async Task<CustomerEligibilityResultDto?> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.CanCreateOrders ||
            string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            throw new UnauthorizedAccessException(
                "No tienes permisos para consultar "
                + "la disponibilidad de clientes para pedidos.");
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador del cliente no es válido.",
                nameof(customerId));
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == customerId,
                cancellationToken);

        if (customer is null)
        {
            return null;
        }

        return _customerEligibility.Evaluate(customer);
    }
}