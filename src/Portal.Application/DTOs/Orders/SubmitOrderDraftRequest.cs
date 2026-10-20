using System;

namespace Portal.Application.DTOs.Orders;

public sealed class SubmitOrderDraftRequest
{
    public Guid Id { get; init; }

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}