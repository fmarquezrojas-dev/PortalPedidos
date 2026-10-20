using System;

namespace ErpSimulator.Api.Entities;

public sealed class ErpOrderReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IdempotencyKey { get; set; }

    public Guid ExternalReference { get; set; }

    public string PayloadHash { get; set; } = string.Empty;

    public string RequestJson { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? ExternalOrderId { get; set; }

    public string ResultCode { get; set; } = string.Empty;

    public string ResultMessage { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; }
}