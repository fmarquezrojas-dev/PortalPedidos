using System;
namespace Portal.Application.DTOs.Audit;

public sealed class AuditSearchDto
{
    public string? Search { get; set; }
    public string? Module { get; set; }
    public string? EventCode { get; set; }
    public string? ActorType { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? CorrelationId { get; set; }
    public int PageNumber { get; set; } = 1;
}
