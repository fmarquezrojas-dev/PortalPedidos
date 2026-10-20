using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Erp;

namespace Portal.Application.Interfaces.Erp;

public interface IErpOrderClient
{
    Task<ErpOrderResponseDto> SubmitAsync(
        Guid externalReference,
        Guid idempotencyKey,
        string payloadJson,
        string payloadHash,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    Task<ErpOrderResponseDto?> GetResultAsync(
        Guid externalReference,
        string expectedPayloadHash,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}