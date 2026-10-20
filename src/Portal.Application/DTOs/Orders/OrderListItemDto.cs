using System;
using Portal.Domain.Enums;

namespace Portal.Application.DTOs.Orders;

public sealed class OrderListItemDto
{
    public Guid Id { get; init; }

    public long PublicNumber { get; init; }

    public string CustomerCode { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public decimal Subtotal { get; init; }

    public string Currency { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}