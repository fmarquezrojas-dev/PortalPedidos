using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Customers;

namespace Portal.Application.Interfaces.Customers;

public interface ICustomerPortfolioListQuery
{
    Task<CustomerPortfolioListPageDto> SearchAsync(
        CustomerPortfolioListFilterDto filters,
        CancellationToken cancellationToken = default);
}