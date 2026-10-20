using System;
using Portal.Domain.Enums;

namespace Portal.Application.DTOs.Orders;

public sealed class OrderHistoryItemDto
{
    public OrderStatus? PreviousStatus { get; init; }

    public OrderStatus NewStatus { get; init; }

    public string Reason { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; }
}