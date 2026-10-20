using System;

namespace Portal.Application.DTOs.Catalog;

public sealed class CatalogSyncResultDto
{
    public long CatalogVersion { get; init; }

    public DateTime CompletedAtUtc { get; init; }

    public int CustomersReceived { get; init; }

    public int ProductsReceived { get; init; }

    public int StocksReceived { get; init; }
}