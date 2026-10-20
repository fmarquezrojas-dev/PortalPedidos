using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Orders;

namespace Portal.Application.Interfaces.Orders;

public interface IOrderDraftService
{
    Task<OrderDraftFormDataDto> GetFormDataAsync(
        CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(
        CreateOrderDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderDraftEditDto?> GetEditableAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        UpdateOrderDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(
        CancelOrderDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> QueueAsync(
    SubmitOrderDraftRequest request,
    CancellationToken cancellationToken = default);
}