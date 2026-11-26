namespace Portal.Application.DTOs.Customers;

public enum PortfolioFilter { Holder, Substitute, Expiring }

public sealed class CommercialAccount
{
    public Guid CustomerId { get; init; }
    public Guid AssignmentId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public string SupervisorId { get; init; } = "";
    public string HolderId { get; init; } = "";
    public string Holder { get; init; } = "";
    public string? SubstituteId { get; init; }
    public string? Substitute { get; init; }
    public DateTime? EndsAtUtc { get; init; }
}

public sealed record CommercialPortfolioPage(IReadOnlyList<CommercialAccount> Items, string UserId,
    bool TeamView, string? Search, PortfolioFilter? Filter, int Total, int Page, int Pages,
    int Holders, int Substitutes, int Expiring, DateTime ExpiringUntilUtc);

public sealed class CoverageEntry
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public Guid AssignmentId { get; init; }
    public string Holder { get; init; } = "";
    public string SubstituteId { get; init; } = "";
    public string Substitute { get; init; } = "";
    public string GrantedBy { get; init; } = "";
    public DateTime GrantedAtUtc { get; init; }
    public string Reason { get; init; } = "";
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public string? RevokedBy { get; init; }
    public string? RevokeReason { get; init; }
    public string? Problem { get; init; }
}
