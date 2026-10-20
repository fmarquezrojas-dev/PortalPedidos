using System;

namespace Portal.Application.DTOs.Catalog;

public class ProductCatalogItemDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string UnitCode { get; init; } = string.Empty;

    public decimal UnitPrice { get; init; }

    public decimal? AvailableStock { get; init; }

    public DateTime? StockObservedAtUtc { get; init; }
}