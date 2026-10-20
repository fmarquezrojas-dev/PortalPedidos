using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Orders;

namespace Portal.Application.Interfaces.Orders;

public interface ICopyRejectedOrderService
{
    Task<CopyRejectedOrderPreviewDto?> GetPreviewAsync(
        Guid sourceOrderId,
        CancellationToken cancellationToken = default);

    Task<Guid?> CreateAsync(
        Guid sourceOrderId,
        Guid draftId,
        long expectedCatalogVersion,
        DateOnly requestedDate,
        CancellationToken cancellationToken = default);
}