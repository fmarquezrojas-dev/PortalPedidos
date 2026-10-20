using System;

namespace Portal.Domain.Entities;

public class CatalogSyncState
{
    public int Id { get; set; } = 1;

    public DateTime? LastSuccessAtUtc { get; set; }

    public long CurrentVersion { get; set; }

    public string? LastErrorCode { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}