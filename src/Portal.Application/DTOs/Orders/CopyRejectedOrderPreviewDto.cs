using System;
using System.Collections.Generic;
using System.Linq;

namespace Portal.Application.DTOs.Orders;

public class CopyRejectedOrderPreviewDto
{
    public Guid SourceOrderId { get; set; }

    public long SourcePublicNumber { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string Currency { get; set; } = "PEN";

    public long CatalogVersion { get; set; }

    public DateOnly TodayInPeru { get; set; }

    public decimal OriginalSubtotal { get; set; }

    public decimal NewSubtotal { get; set; }

    public List<CopyRejectedOrderLineDto> Lines { get; set; } = new();

    public List<string> Problems { get; set; } = new();

    public bool CanCopy => Problems.Count == 0;

    public bool PricesChanged => Lines.Any(line =>
        line.CurrentUnitPrice.HasValue &&
        line.CurrentUnitPrice.Value != line.OriginalUnitPrice);
}

public class CopyRejectedOrderLineDto
{
    public Guid ProductId { get; set; }

    public string ItemCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal OriginalUnitPrice { get; set; }

    public decimal? CurrentUnitPrice { get; set; }

    public decimal? CurrentLineAmount { get; set; }
}