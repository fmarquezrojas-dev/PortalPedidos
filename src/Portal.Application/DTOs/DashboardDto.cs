using System;
using System.Collections.Generic;
using Portal.Domain.Enums;

namespace Portal.Application.DTOs.Dashboard;

public class DashboardDto
{
    public bool ShowsAllOrders { get; set; }

    public bool ShowsTeamOrders { get; set; }

    public bool CanCreateOrders { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    public int TotalOrders { get; set; }

    public int DraftOrders { get; set; }

    public int QueuedOrders { get; set; }

    public int ConfirmedOrders { get; set; }

    public int RejectedOrders { get; set; }

    public int CancelledOrders { get; set; }

    public long CatalogVersion { get; set; }

    public DateTime? CatalogLastSuccessAtUtc { get; set; }

    public string? CatalogLastErrorCode { get; set; }

    public bool CatalogAvailable { get; set; }

    public bool CatalogIsStale { get; set; }

    public int? CatalogAgeMinutes { get; set; }

    public int ActiveIntegrationMessages { get; set; }

    public int UnknownIntegrationMessages { get; set; }

    public int DeadLetterIntegrationMessages { get; set; }

    public int OverdueIntegrationMessages { get; set; }

    public DateTime? OldestUnresolvedMessageAtUtc { get; set; }

    public IReadOnlyList<DashboardOrderDto> RecentOrders { get; set; }
        = Array.Empty<DashboardOrderDto>();
}

public class DashboardOrderDto
{
    public Guid Id { get; set; }

    public long PublicNumber { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public OrderStatus Status { get; set; }

    public string Currency { get; set; } = "PEN";

    public decimal Subtotal { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}