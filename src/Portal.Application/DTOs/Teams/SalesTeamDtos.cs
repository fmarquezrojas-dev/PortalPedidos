using System;
using System.Collections.Generic;

namespace Portal.Application.DTOs.Teams;

public class SalesTeamSummaryDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SupervisorEmail { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int MemberCount { get; set; }
}

public sealed class SalesTeamPageDto
{
    public IReadOnlyList<SalesTeamSummaryDto> Items { get; set; }
        = Array.Empty<SalesTeamSummaryDto>();

    public bool CanCreate { get; set; }

    public int TotalCount { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalPages =>
        Math.Max(
            1,
            (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed class SalesTeamMemberDto
{
    public Guid MembershipId { get; set; }

    public string Email { get; set; } = string.Empty;

    public bool UserIsActive { get; set; }

    public DateTime JoinedAtUtc { get; set; }

    public DateTime? LeftAtUtc { get; set; }
}

public sealed class SalesTeamDetailDto : SalesTeamSummaryDto
{
    public bool CanManage { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public IReadOnlyList<SalesTeamMemberDto> Members { get; set; }
        = Array.Empty<SalesTeamMemberDto>();

    public IReadOnlyList<SalesTeamMemberDto> RecentDepartures { get; set; }
        = Array.Empty<SalesTeamMemberDto>();
}