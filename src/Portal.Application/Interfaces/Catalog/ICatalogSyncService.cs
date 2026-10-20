using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Catalog;

namespace Portal.Application.Interfaces.Catalog;

public interface ICatalogSyncService
{
    Task<CatalogSyncResultDto> SynchronizeAsync(
        CancellationToken cancellationToken = default);
}