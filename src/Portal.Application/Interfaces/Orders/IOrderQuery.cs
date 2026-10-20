using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Orders;
using Portal.Domain.Enums;

namespace Portal.Application.Interfaces.Orders;

public interface IOrderQuery
{
    Task<OrderListPageDto> SearchAsync(
        string? search,
        OrderStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<OrderListPageDto> SearchAsync(OrderSearchFilterDto filters, int pageNumber,
        int pageSize, CancellationToken cancellationToken = default);

    Task<OrderExportDto> ExportAsync(OrderSearchFilterDto filters,
        CancellationToken cancellationToken = default);

    Task<OrderDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
