namespace Portal.Application.DTOs.Security;

public sealed class RoleDirectoryItemDto
{
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool IsRegistered { get; init; }

    public bool IsRecognized { get; init; }

    public bool AccessConfigured { get; init; }

    public int AssignedUserCount { get; init; }

    public int ActiveAssignedUserCount { get; init; }
}