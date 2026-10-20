using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Customers;

public class CustomerDirectoryItemDto
{
    public Guid Id { get; set; }

    public string ExternalCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public bool IsEligibleForOrders { get; set; }

    public string? EligibilityCode { get; set; }

    public string? EligibilityMessage { get; set; }
}

public class CustomerDirectoryPageDto
{
    public string? Search { get; set; }

    public bool? IsActive { get; set; }

    public int TotalCount { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public long CatalogVersion { get; set; }

    public DateTime? LastSuccessAtUtc { get; set; }

    public bool CatalogIsFresh { get; set; }

 

    public IReadOnlyList<CustomerDirectoryItemDto> Items { get; set; }
        = Array.Empty<CustomerDirectoryItemDto>();

    public int TotalPages => Math.Max(
        1,
        (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Search) ||
        IsActive.HasValue;

    public int FirstItemNumber =>
        TotalCount == 0
            ? 0
            : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber =>
        Items.Count == 0
            ? 0
            : FirstItemNumber + Items.Count - 1;


   
}