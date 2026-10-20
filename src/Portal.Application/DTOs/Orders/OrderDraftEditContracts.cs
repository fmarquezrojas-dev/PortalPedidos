using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Orders;

public sealed class OrderDraftEditDto
{
    public Guid Id { get; init; }

    public long PublicNumber { get; init; }

    public Guid CustomerId { get; init; }

    public DateOnly RequestedDate { get; init; }

    public decimal Subtotal { get; init; }

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();

    public IReadOnlyList<CreateOrderDraftLineRequest> Lines { get; init; }
        = Array.Empty<CreateOrderDraftLineRequest>();
}

public sealed class UpdateOrderDraftRequest
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public DateOnly RequestedDate { get; init; }

    public long CatalogVersion { get; init; }

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();

    public bool AcceptCurrentPrices { get; init; }

    public IReadOnlyList<CreateOrderDraftLineRequest> Lines { get; init; }
        = Array.Empty<CreateOrderDraftLineRequest>();
}