using System;
using System.Threading;
using System.Threading.Tasks;
using Portal.Application.DTOs.Teams;

namespace Portal.Application.Interfaces.Teams;

public interface ISalesTeamService
{
    Task<SalesTeamPageDto> GetPageAsync(
        int pageNumber,
        CancellationToken cancellationToken = default);

    Task<SalesTeamDetailDto?> GetDetailsAsync(
        Guid teamId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(
        string code,
        string name,
        string supervisorEmail,
        CancellationToken cancellationToken = default);

    Task AddMemberAsync(
        Guid teamId,
        string sellerEmail,
        byte[] rowVersion,
        CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(
        Guid teamId,
        Guid membershipId,
        byte[] rowVersion,
        CancellationToken cancellationToken = default);
}