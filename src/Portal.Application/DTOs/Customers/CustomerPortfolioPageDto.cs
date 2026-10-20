using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Customers;

public sealed class CustomerPortfolioPageDto
{
    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public bool CustomerIsActive { get; set; }

    public CustomerPortfolioAssignmentDto? CurrentAssignment
    {
        get;
        set;
    }

    public IReadOnlyList<CustomerPortfolioAssignmentDto> History
    {
        get;
        set;
    } = Array.Empty<CustomerPortfolioAssignmentDto>();

    public int ClosedAssignmentCount { get; set; }
}

public sealed class CustomerPortfolioAssignmentDto
{
    public Guid Id { get; set; }

    public string SellerEmail { get; set; } = string.Empty;

    public string TeamCode { get; set; } = string.Empty;

    public string TeamName { get; set; } = string.Empty;

    public bool SellerIsActive { get; set; }

    public bool SellerHasRole { get; set; }

    public bool MembershipIsActive { get; set; }

    public bool TeamIsActive { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    public string AssignedByEmail { get; set; } = string.Empty;

    public string AssignmentReason { get; set; } = string.Empty;

    public DateTime? EndedAtUtc { get; set; }

    public string? EndedByEmail { get; set; }

    public string? EndReason { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public bool IsOperational =>
        !EndedAtUtc.HasValue &&
        SellerIsActive &&
        SellerHasRole &&
        MembershipIsActive &&
        TeamIsActive;
}