using System;

namespace Portal.Domain.Entities;

public class IntegrationAttempt
{
    public long Id { get; set; }

    public Guid MessageId { get; set; }

    public int AttemptNumber { get; set; }

    public string Operation { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public int? HttpStatus { get; set; }

    public string ResultCode { get; set; } = string.Empty;

    public string? ErrorSummary { get; set; }

    public Guid CorrelationId { get; set; }

    public long? DurationMs { get; set; }
}