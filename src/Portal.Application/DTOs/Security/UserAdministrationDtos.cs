using System;
using System.Collections.Generic;
namespace Portal.Application.DTOs.Security;

public sealed class ManagedUserItemDto
{
    public string Id { get; init; } = "";
    public string Email { get; init; } = "";
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}
public sealed class ManagedUserPageDto
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; } = 20;
    public int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public IReadOnlyList<ManagedUserItemDto> Items { get; init; } = Array.Empty<ManagedUserItemDto>();
}
public sealed class ManagedUserEditDto
{
    public string Id { get; init; } = "";
    public string Email { get; init; } = "";
    public bool IsActive { get; init; }
    public string ConcurrencyStamp { get; init; } = "";
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}
public sealed class CreateManagedUserRequest
{
    public string Email { get; init; } = "";
    public string Password { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public string Reason { get; init; } = "";
}
public sealed class UpdateManagedUserRequest
{
    public string Id { get; init; } = "";
    public string ExpectedConcurrencyStamp { get; init; } = "";
    public bool IsActive { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public string Reason { get; init; } = "";
}
