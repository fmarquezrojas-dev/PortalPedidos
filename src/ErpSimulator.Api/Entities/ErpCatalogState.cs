using System;

namespace ErpSimulator.Api.Entities;

public class ErpCatalogState
{
    public int Id { get; set; } = 1;

    public long CurrentVersion { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}