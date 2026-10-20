using System;

namespace Portal.Web.Models.Users;

public class UserListItemViewModel
{
    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}