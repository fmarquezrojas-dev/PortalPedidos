using System;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Application.Interfaces.Customers;

public interface ICustomerPortfolioService
{
    Task<Guid> AssignAsync(
        Guid customerId,
        string sellerEmail,
        string reason,
        Guid? expectedAssignmentId,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        Guid customerId,
        Guid expectedAssignmentId,
        byte[] expectedRowVersion,
        string reason,
        CancellationToken cancellationToken = default);
}