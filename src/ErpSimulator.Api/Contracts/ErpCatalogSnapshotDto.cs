using System;
using System.Collections.Generic;

namespace ErpSimulator.Api.Contracts;

public class ErpCatalogSnapshotDto
{
    public long CatalogVersion { get; init; }

    public DateTime ObservedAtUtc { get; init; }

    public string Currency { get; init; } = "PEN";

    public string WarehouseCode { get; init; } = "01";

    public IReadOnlyList<ErpCustomerDto> Customers { get; init; }
        = Array.Empty<ErpCustomerDto>();

    public IReadOnlyList<ErpItemDto> Items { get; init; }
        = Array.Empty<ErpItemDto>();

    public IReadOnlyList<ErpStockDto> Stocks { get; init; }
        = Array.Empty<ErpStockDto>();
}