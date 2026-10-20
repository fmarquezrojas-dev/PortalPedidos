using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Catalog;

public sealed class ProductCatalogPageDto
{
    public string Search { get; init; } = string.Empty;

    public IReadOnlyList<ProductCatalogItemDto> Items { get; init; }
        = Array.Empty<ProductCatalogItemDto>();

    public int TotalCount { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int TotalPages =>
        PageSize <= 0
            ? 1
            : Math.Max(
                1,
                (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public long CatalogVersion { get; init; }

    public DateTime? LastSuccessAtUtc { get; init; }

    public DateTime? CatalogObservedAtUtc { get; init; }

    public string? LastErrorCode { get; init; }
}