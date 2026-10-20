using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Customers;

public sealed class CustomerPortfolioListPageDto
{
    public CustomerPortfolioListFilterDto Filters { get; set; } = new();

    public int TotalCount { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public IReadOnlyList<CustomerPortfolioListItemDto> Items { get; set; }
        = Array.Empty<CustomerPortfolioListItemDto>();

    public int TotalPages =>
        PageSize <= 0
            ? 1
            : Math.Max(
                1,
                (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Filters.Search) ||
        !string.IsNullOrWhiteSpace(Filters.SellerSearch) ||
        !string.IsNullOrWhiteSpace(Filters.TeamSearch) ||
        Filters.CustomerIsActive.HasValue ||
        Filters.AssignmentStatus.HasValue;

    public int FirstItemNumber =>
        Items.Count == 0
            ? 0
            : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber =>
        Items.Count == 0
            ? 0
            : FirstItemNumber + Items.Count - 1;
}