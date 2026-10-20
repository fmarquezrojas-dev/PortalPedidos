using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Customers;

public sealed class CustomerPortfolioListItemDto
{
    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public bool CustomerIsActive { get; set; }

    public bool IsEligibleForOrders { get; set; }

    public string? EligibilityCode { get; set; }

    public string? EligibilityMessage { get; set; }

    public CustomerPortfolioAssignmentStatus AssignmentStatus { get; set; }
        = CustomerPortfolioAssignmentStatus.Unassigned;

    public string? SellerEmail { get; set; }

    public string? TeamCode { get; set; }

    public string? TeamName { get; set; }

    public DateTime? AssignedAtUtc { get; set; }

    public IReadOnlyList<string> ReviewReasons { get; set; }
        = Array.Empty<string>();
}