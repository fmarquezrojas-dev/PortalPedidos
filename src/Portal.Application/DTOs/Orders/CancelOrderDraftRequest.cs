using System;

namespace Portal.Application.DTOs.Orders;

public sealed class CancelOrderDraftRequest
{
    public Guid Id { get; init; }

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();

    public string Reason { get; init; } = string.Empty;
}