using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Orders;

public sealed class OrderDetailDto
{
    public string? Creator { get; init; }
    public string? HistoricalHolder { get; init; }
    public OrderListItemDto Order { get; init; } = new();

    public DateOnly RequestedDate { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public DateTime? SubmittedAtUtc { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string? ExternalOrderId { get; init; }

    public IReadOnlyList<OrderLineDto> Lines { get; init; }
        = Array.Empty<OrderLineDto>();

    public IReadOnlyList<OrderHistoryItemDto> History { get; init; }
        = Array.Empty<OrderHistoryItemDto>();

    public System.Guid? SalesTeamId { get; set; }

    public string? SalesTeamCode { get; set; }

    public string? SalesTeamName { get; set; }
}
