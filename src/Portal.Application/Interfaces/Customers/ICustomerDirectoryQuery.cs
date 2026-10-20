using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Customers;

namespace Portal.Application.Interfaces.Customers;

public interface ICustomerDirectoryQuery
{
    Task<CustomerDirectoryPageDto> SearchAsync(
        string? search,
        bool? isActive,
        int pageNumber,
        CancellationToken cancellationToken = default);
}