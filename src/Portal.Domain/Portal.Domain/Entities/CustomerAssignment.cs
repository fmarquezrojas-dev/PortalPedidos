using System;

namespace Portal.Domain.Entities;

public class CustomerAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerId { get; set; }

    // Membresía concreta del vendedor en un equipo.
    public Guid SalesTeamMemberId { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    public string AssignedByUserId { get; set; } = string.Empty;

    public string AssignmentReason { get; set; } = string.Empty;

    // Null significa que esta asignación todavía no se ha cerrado.
    public DateTime? EndedAtUtc { get; set; }

    public string? EndedByUserId { get; set; }

    public string? EndReason { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}