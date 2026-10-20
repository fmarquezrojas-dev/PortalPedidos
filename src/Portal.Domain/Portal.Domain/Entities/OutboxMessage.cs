using System;
using Portal.Domain.Enums;

namespace Portal.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }

    public Guid IdempotencyKey { get; set; }

    public string Provider { get; set; } = "Simulator";

    public int SchemaVersion { get; set; } = 1;

    public string PayloadJson { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public IntegrationStatus Status { get; set; }
        = IntegrationStatus.Pending;

    public int AttemptCount { get; set; }

    public int RecoveryCycle { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public Guid? LeaseOwner { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}