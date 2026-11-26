namespace Portal.Domain.Entities;

public class CustomerCoverage
{
    public Guid Id { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid SubstituteMemberId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; } // Medianoche exclusiva del día siguiente en Lima.
    public string Reason { get; set; } = string.Empty;
    public string GrantedByUserId { get; set; } = string.Empty;
    public DateTime GrantedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? RevokeReason { get; set; }
}
