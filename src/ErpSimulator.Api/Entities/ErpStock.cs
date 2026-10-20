using System;

namespace ErpSimulator.Api.Entities;

public class ErpStock
{
    public string ItemCode { get; set; } = string.Empty;

    public string WarehouseCode { get; set; } = "01";

    public decimal OnHand { get; set; }

    public decimal Reserved { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}