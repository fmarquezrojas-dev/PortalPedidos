using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Customers;

namespace Portal.Application.Interfaces.Customers;

public interface ICustomerPortfolioQuery
{
    Task<CustomerPortfolioPageDto?> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}