using System;

namespace ErpSimulator.Api.Contracts.Orders;

public sealed class ErpOrderResultDto
{
    public Guid ExternalReference { get; init; }

    public string PayloadHash { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? ExternalOrderId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTime ProcessedAtUtc { get; init; }
}