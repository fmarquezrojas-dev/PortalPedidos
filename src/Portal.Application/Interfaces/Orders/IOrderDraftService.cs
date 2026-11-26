using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Orders;

namespace Portal.Application.Interfaces.Orders;

public interface IOrderDraftService
{
    Task<CaptureSearchPage<CustomerOptionDto>> SearchCustomersAsync(string term, int offset, CancellationToken cancellationToken);

    Task<CaptureSearchPage<ProductOptionDto>> SearchProductsAsync(string term, int offset, CancellationToken cancellationToken);

    Task<OrderDraftFormDataDto> GetFormDataAsync(
        Guid? customerId, Guid[] productIds,
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
