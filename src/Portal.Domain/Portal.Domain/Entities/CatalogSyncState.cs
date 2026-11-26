using System;

namespace Portal.Domain.Entities;

public class CatalogSyncState
{
    public int Id { get; set; } = 1;

    public DateTime? LastSuccessAtUtc { get; set; }

    public long CurrentVersion { get; set; }

    public string? LastErrorCode { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public bool IsFreshForSubmission(DateTime nowUtc) =>
        CurrentVersion > 0 &&
        LastSuccessAtUtc >= nowUtc.AddMinutes(-5) &&
        LastSuccessAtUtc <= nowUtc.AddMinutes(1);
}
