using System;

namespace Portal.Domain.Entities;

public class SalesTeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SalesTeamId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime JoinedAtUtc { get; set; }

    public DateTime? LeftAtUtc { get; set; }
}