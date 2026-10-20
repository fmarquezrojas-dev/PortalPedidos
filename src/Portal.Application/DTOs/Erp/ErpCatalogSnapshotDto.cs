using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Erp;

public sealed class ErpCatalogSnapshotDto
{
    public long CatalogVersion { get; init; }

    public DateTime ObservedAtUtc { get; init; }

    public string Currency { get; init; } = string.Empty;

    public string WarehouseCode { get; init; } = string.Empty;

    public IReadOnlyList<ErpCustomerDto> Customers { get; init; }
        = Array.Empty<ErpCustomerDto>();

    public IReadOnlyList<ErpItemDto> Items { get; init; }
        = Array.Empty<ErpItemDto>();

    public IReadOnlyList<ErpStockDto> Stocks { get; init; }
        = Array.Empty<ErpStockDto>();
}