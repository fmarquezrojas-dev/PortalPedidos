using System;
using System.Linq.Expressions;
using Portal.Application.DTOs.Customers;
using Portal.Domain.Entities;

namespace Portal.Application.Interfaces.Customers;

public interface ICustomerEligibilityService
{
    CustomerEligibilityResultDto Evaluate(Customer? customer);

    Expression<Func<Customer, bool>> GetEligibleCustomerPredicate();
}