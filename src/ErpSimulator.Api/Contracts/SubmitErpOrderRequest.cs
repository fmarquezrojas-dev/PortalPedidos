using System;
using System.Collections.Generic;

namespace ErpSimulator.Api.Contracts.Orders;

public sealed class SubmitErpOrderRequest
{
    public int SchemaVersion { get; init; }

    public Guid ExternalReference { get; init; }

    public string CustomerCode { get; init; } = string.Empty;

    public DateOnly RequestedDate { get; init; }

    public string Currency { get; init; } = string.Empty;

    public string WarehouseCode { get; init; } = string.Empty;

    public decimal Subtotal { get; init; }

    public List<SubmitErpOrderLineRequest> Lines { get; init; } = new();
}

public sealed class SubmitErpOrderLineRequest
{
    public int LineNumber { get; init; }

    public string ItemCode { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineAmount { get; init; }
}