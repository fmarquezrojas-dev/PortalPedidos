using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Orders;

public sealed class CustomerOptionDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

public sealed class ProductOptionDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsActive { get; init; }

    public decimal UnitPrice { get; init; }
}

public sealed record CaptureSearchPage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed class OrderDraftFormDataDto
{
    public long CatalogVersion { get; init; }

    public DateOnly TodayInPeru { get; init; }

    public CustomerOptionDto? Customer { get; init; }

    public bool CustomerIsEligible { get; init; }

    public IReadOnlyList<ProductOptionDto> Products { get; init; }
        = Array.Empty<ProductOptionDto>();
}

public sealed class CreateOrderDraftRequest
{
    public Guid DraftId { get; init; }

    public Guid CustomerId { get; init; }

    public DateOnly RequestedDate { get; init; }

    public long CatalogVersion { get; init; }

    public IReadOnlyList<CreateOrderDraftLineRequest> Lines { get; init; }
        = Array.Empty<CreateOrderDraftLineRequest>();
}

public sealed class CreateOrderDraftLineRequest
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}