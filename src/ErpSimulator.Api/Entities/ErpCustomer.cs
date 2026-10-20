using System;

namespace ErpSimulator.Api.Entities;

public class ErpCustomer
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}