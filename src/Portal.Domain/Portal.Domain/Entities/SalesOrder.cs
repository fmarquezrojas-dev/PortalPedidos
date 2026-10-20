using System;
using Portal.Domain.Enums;

namespace Portal.Domain.Entities;

public class SalesOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public long PublicNumber { get; set; }

    public string OwnerUserId { get; set; } = string.Empty;

    // Equipo al que se atribuye el pedido desde su creación.
    // Null permite conservar pedidos anteriores y pedidos sin equipo.
    public Guid? SalesTeamId { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public DateOnly RequestedDate { get; set; }

    public string Currency { get; set; } = "PEN";

    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    public decimal Subtotal { get; set; }

    public long CatalogVersion { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public string Provider { get; set; } = "Simulator";

    public string? ExternalOrderId { get; set; }

    public Guid? ReplacesOrderId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}