using System;
using Portal.Domain.Enums;

namespace Portal.Domain.Entities;

public class OrderStatusHistory
{
    public long Id { get; set; }

    public Guid OrderId { get; set; }

    public OrderStatus? PreviousStatus { get; set; }

    public OrderStatus NewStatus { get; set; }

    public string? ActorUserId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public Guid CorrelationId { get; set; }
}