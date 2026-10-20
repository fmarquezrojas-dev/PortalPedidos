using System;

namespace ErpSimulator.Api.Entities;

public class ErpItem
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string UnitCode { get; set; } = "UND";

    public decimal UnitPrice { get; set; }

    public bool Active { get; set; } = true;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}