using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Audit;

public class AuditListItemDto
{
    public long Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string? ActorUserId { get; set; }

    public string? ActorEmail { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;
}

public class AuditPageDto
{
    public string? Search { get; set; }

    public AuditSearchDto Filters { get; set; } = new();

    public IReadOnlyList<AuditListItemDto> Items { get; set; }
        = Array.Empty<AuditListItemDto>();

    public int TotalCount { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalPages => Math.Max(
        1,
        (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}

public class AuditDetailDto : AuditListItemDto
{
    public Guid CorrelationId { get; set; }

    public string SummaryJson { get; set; } = "{}";
}
