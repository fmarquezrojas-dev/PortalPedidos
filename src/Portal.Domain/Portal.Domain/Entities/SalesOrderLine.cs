using System;

namespace Portal.Domain.Entities;

public class SalesOrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }

    public int LineNumber { get; set; }

    public Guid ProductId { get; set; }

    public string ItemCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineAmount { get; set; }

    public string WarehouseCode { get; set; } = "01";
}