using Portal.Domain.Entities;
using Portal.Domain.Enums;

namespace Portal.Application.DTOs.Integration;

// El valor ascendente también define el orden de prioridad en la bandeja.
public enum AttentionCategory { Review, Uncertain, ExpiredLease, Delayed, Monitoring }

public sealed record OperationalItem(
    Guid MessageId, long PublicNumber, string CustomerName, IntegrationStatus Status,
    AttentionCategory Category, DateTime CreatedAtUtc, DateTime? LastAttemptAtUtc,
    DateTime NextAttemptAtUtc, DateTime? LeaseUntilUtc, DateTime? CompletedAtUtc,
    string? LastErrorCode, string? ExternalOrderId);

public sealed record OperationalAttention(
    DateTime AsOfUtc, long? OrderNumber, AttentionCategory? Category,
    IReadOnlyDictionary<AttentionCategory, int> Counts, int TotalCount, int PageNumber,
    int TotalPages, IReadOnlyList<OperationalItem> Items, CatalogSyncState? Catalog);
