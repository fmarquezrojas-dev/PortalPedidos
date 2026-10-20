using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Customers;

namespace Portal.Application.Interfaces.Customers;

public interface ICustomerEligibilityQuery
{
    Task<CustomerEligibilityResultDto?> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}