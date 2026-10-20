using System;
using System.Collections.Generic;
using Portal.Domain.Enums;

namespace Portal.Application.DTOs.Integration;

public sealed class IntegrationMonitorItemDto
{
    public Guid MessageId { get; init; }

    public Guid OrderId { get; init; }

    public long PublicNumber { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public IntegrationStatus Status { get; init; }

    public int AttemptCount { get; init; }

    public int RecordedHttpAttempts { get; init; }

    public int RecoveryCycle { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime NextAttemptAtUtc { get; init; }

    public DateTime? LeaseUntilUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public string? LastErrorCode { get; init; }

    public string? ExternalOrderId { get; init; }
}

public sealed class IntegrationMonitorPageDto
{
    public long? OrderNumber { get; init; }

    public IntegrationStatus? Status { get; init; }

    public IReadOnlyList<IntegrationMonitorItemDto> Items
    { get; init; } = Array.Empty<IntegrationMonitorItemDto>();

    public int TotalCount { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int TotalPages => Math.Max(
        1,
        (int)Math.Ceiling((double)TotalCount / PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed class IntegrationAttemptItemDto
{
    public int AttemptNumber { get; init; }

    public string Operation { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; init; }

    public int? HttpStatus { get; init; }

    public string ResultCode { get; init; } = string.Empty;

    public string? ErrorSummary { get; init; }

    public long? DurationMs { get; init; }

    public Guid CorrelationId { get; init; }
}

public sealed class IntegrationMonitorDetailDto
{
    public IntegrationMonitorItemDto Message { get; init; } = new();

    public IReadOnlyList<IntegrationAttemptItemDto> Attempts
    { get; init; } = Array.Empty<IntegrationAttemptItemDto>();
}