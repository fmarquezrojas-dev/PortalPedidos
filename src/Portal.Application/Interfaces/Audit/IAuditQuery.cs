using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Audit;

namespace Portal.Application.Interfaces.Audit;

public interface IAuditQuery
{
    Task<AuditPageDto> SearchAsync(
        string? search,
        int pageNumber,
        CancellationToken cancellationToken = default);

    Task<AuditPageDto> SearchAsync(
        AuditSearchDto filters,
        CancellationToken cancellationToken = default);

    Task<AuditDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);
}
