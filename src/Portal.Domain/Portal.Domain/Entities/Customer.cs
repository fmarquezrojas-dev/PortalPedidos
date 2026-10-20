using System;

namespace Portal.Domain.Entities;

public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ExternalCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; }

    public long CatalogVersion { get; set; }
}