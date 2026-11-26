using Portal.Application.DTOs.Customers;
namespace Portal.Web.Models.Customers;
public sealed class CustomerPortfolioDetailsViewModel
{
    public CustomerPortfolioPageDto Portfolio { get; init; } = new();
    public CustomerEligibilityResultDto? Eligibility { get; init; }
    public bool CanCreateOrders { get; init; }
    public IReadOnlyList<CoverageEntry> Coverages { get; init; } = [];
    public DateTime AsOfUtc { get; init; }
}
