using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Catalog;

namespace Portal.Application.Interfaces.Catalog;

public interface IProductCatalogQuery
{
    Task<ProductCatalogPageDto> SearchAsync(
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}