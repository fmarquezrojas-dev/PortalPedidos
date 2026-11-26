using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Integration;
using Portal.Domain.Enums;

namespace Portal.Application.Interfaces.Integration;

public interface IIntegrationMonitorQuery
{
    Task<OperationalAttention> GetAttentionAsync(long? orderNumber, AttentionCategory? category,
        int pageNumber, CancellationToken cancellationToken = default);

    Task<IntegrationMonitorPageDto> SearchAsync(
        long? orderNumber,
        IntegrationStatus? status,
        int pageNumber,
        CancellationToken cancellationToken = default);

    Task<IntegrationMonitorDetailDto?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);
}
