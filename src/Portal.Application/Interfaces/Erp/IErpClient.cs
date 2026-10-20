using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Erp;

namespace Portal.Application.Interfaces.Erp;

public interface IErpClient
{
    Task<ErpCatalogSnapshotDto> GetCatalogAsync(
        CancellationToken cancellationToken = default);
}