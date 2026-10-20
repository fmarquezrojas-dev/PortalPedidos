using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Integration;

namespace Portal.Application.Interfaces.Integration;

public interface IIntegrationRecoveryService
{
    Task<IntegrationRecoveryPreviewDto?> GetPreviewAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<bool> RecoverAsync(
        Guid messageId,
        byte[] rowVersion,
        string reason,
        CancellationToken cancellationToken = default);
}