using System;
using System.Collections.Generic;
using Portal.Domain.Enums;

namespace Portal.Application.DTOs.Orders;

public sealed class OrderListPageDto
{
    public string Search { get; init; } = string.Empty;

    public OrderStatus? Status { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public IReadOnlyList<OrderListItemDto> Items { get; init; }
        = Array.Empty<OrderListItemDto>();

    public int TotalCount { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int TotalPages =>
        PageSize <= 0
            ? 1
            : Math.Max(
                1,
                (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public bool ShowsAllOrders { get; set; }

    public bool ShowsTeamOrders { get; set; }
}
