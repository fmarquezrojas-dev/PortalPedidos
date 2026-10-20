using System;

namespace Portal.Application.DTOs.Erp;

public sealed class ErpOrderResponseDto
{
    public Guid ExternalReference { get; init; }

    public string PayloadHash { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? ExternalOrderId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset ProcessedAtUtc { get; init; }
}