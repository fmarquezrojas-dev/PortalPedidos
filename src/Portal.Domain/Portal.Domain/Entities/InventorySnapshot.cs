using System;

namespace Portal.Domain.Entities;

public class InventorySnapshot
{
    public Guid ProductId { get; set; }

    public string WarehouseCode { get; set; } = "01";

    public decimal Available { get; set; }

    public DateTime ObservedAtUtc { get; set; }

    public long CatalogVersion { get; set; }
}