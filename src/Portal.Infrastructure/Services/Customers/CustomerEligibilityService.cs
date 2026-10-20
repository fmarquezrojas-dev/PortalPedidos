using System;
using System.Linq.Expressions;
using Portal.Application.DTOs.Customers;
using Portal.Application.Interfaces.Customers;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Services.Customers;

public sealed class CustomerEligibilityService
    : ICustomerEligibilityService
{
    private static readonly Expression<Func<Customer, bool>>
        EligibilityPredicate = customer => customer.IsActive;

    private static readonly Func<Customer, bool>
        MatchesEligibility = EligibilityPredicate.Compile();

    public CustomerEligibilityResultDto Evaluate(Customer? customer)
    {
        if (customer is null)
        {
            return CustomerEligibilityResultDto.Unavailable();
        }

        if (!MatchesEligibility(customer))
        {
            return CustomerEligibilityResultDto.Inactive();
        }

        return CustomerEligibilityResultDto.Eligible();
    }

    public Expression<Func<Customer, bool>>
        GetEligibleCustomerPredicate()
    {
        return EligibilityPredicate;
    }
}