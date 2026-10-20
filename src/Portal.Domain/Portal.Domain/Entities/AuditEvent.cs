using System;

namespace Portal.Domain.Entities;

public class AuditEvent
{
    public long Id { get; set; }

    public string? ActorUserId { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public Guid CorrelationId { get; set; }

    public string SummaryJson { get; set; } = "{}";
}