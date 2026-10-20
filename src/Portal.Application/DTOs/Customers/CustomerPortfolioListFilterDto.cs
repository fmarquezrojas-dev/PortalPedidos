namespace Portal.Application.DTOs.Customers;

public sealed class CustomerPortfolioListFilterDto
{
    public string? Search { get; set; }

    public string? SellerSearch { get; set; }

    public string? TeamSearch { get; set; }

    public bool? CustomerIsActive { get; set; }

    public CustomerPortfolioAssignmentStatus? AssignmentStatus { get; set; }

    public int PageNumber { get; set; } = 1;
}