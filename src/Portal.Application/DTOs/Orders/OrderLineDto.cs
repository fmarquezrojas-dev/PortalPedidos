namespace Portal.Application.DTOs.Orders;

public sealed class OrderLineDto
{
    public int LineNumber { get; init; }

    public string ItemCode { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineAmount { get; init; }

    public string WarehouseCode { get; init; } = string.Empty;
}
