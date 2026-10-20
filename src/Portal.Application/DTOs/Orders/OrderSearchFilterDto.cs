using System;
using System.Collections.Generic;
using Portal.Domain.Enums;
namespace Portal.Application.DTOs.Orders;

public sealed class OrderSearchFilterDto
{
    public string? Search { get; set; }
    public OrderStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed class OrderExportDto
{
    public const int MaximumRows = 2000;
    public IReadOnlyList<OrderListItemDto> Items { get; init; } = Array.Empty<OrderListItemDto>();
    public bool LimitExceeded { get; init; }
}
