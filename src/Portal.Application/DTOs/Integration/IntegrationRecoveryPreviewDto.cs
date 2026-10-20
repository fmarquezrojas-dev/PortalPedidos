using System;
using Portal.Domain.Enums;

namespace Portal.Application.DTOs.Integration;

public sealed class IntegrationRecoveryPreviewDto
{
    public Guid MessageId { get; init; }

    public long PublicNumber { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public IntegrationStatus Status { get; init; }

    public int AttemptCount { get; init; }

    public int RecoveryCycle { get; init; }

    public string? LastErrorCode { get; init; }

    public string RowVersion { get; init; } = string.Empty;

    public string? BlockReason { get; init; }

    public bool CanRecover => BlockReason is null;
}