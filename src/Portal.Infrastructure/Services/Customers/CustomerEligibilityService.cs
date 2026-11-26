using System;
using System.Linq.Expressions;
using Portal.Application.DTOs.Customers;
using Portal.Application.Interfaces.Customers;
using Portal.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Interfaces.Security;
using Portal.Infrastructure.Queries.Customers;

namespace Portal.Infrastructure.Services.Customers;

public sealed class CustomerEligibilityService(CommercialPortfolioQuery portfolio, ICurrentUser actor,
    IConfiguration configuration, TimeProvider clock)
    : ICustomerEligibilityService
{
    private static readonly Expression<Func<Customer, bool>>
        EligibilityPredicate = customer => customer.IsActive;

    private bool Restricted => actor.CanCreateOrders && !actor.IsAdministrator &&
        configuration.GetValue<bool>("CustomerPortfolio:Restricted");

    public async Task<CustomerEligibilityResultDto> EvaluateAsync(Customer? customer, CancellationToken cancellationToken)
    {
        if (customer is null)
        {
            return CustomerEligibilityResultDto.Unavailable();
        }

        if (!customer.IsActive)
        {
            return CustomerEligibilityResultDto.Inactive();
        }

        return Restricted && !await EligibleIds().ContainsAsync(customer.Id, cancellationToken)
            ? CustomerEligibilityResultDto.OutsidePortfolio() : CustomerEligibilityResultDto.Eligible();
    }

    public Expression<Func<Customer, bool>>
        GetEligibleCustomerPredicate()
    {
        if (!Restricted) return EligibilityPredicate;
        var ids = EligibleIds();
        return customer => customer.IsActive && ids.Contains(customer.Id);
    }

    private IQueryable<Guid> EligibleIds() => portfolio.Accounts(clock.GetUtcNow().UtcDateTime)
        .Where(r => r.HolderId == actor.UserId || r.SubstituteId == actor.UserId)
        .Select(r => r.CustomerId);
}
