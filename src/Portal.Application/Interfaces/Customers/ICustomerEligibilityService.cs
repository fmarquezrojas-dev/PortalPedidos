using System;
using System.Linq.Expressions;
using Portal.Application.DTOs.Customers;
using Portal.Domain.Entities;

namespace Portal.Application.Interfaces.Customers;

public interface ICustomerEligibilityService
{
    Task<CustomerEligibilityResultDto> EvaluateAsync(Customer? customer, CancellationToken cancellationToken);

    Expression<Func<Customer, bool>> GetEligibleCustomerPredicate();
}
