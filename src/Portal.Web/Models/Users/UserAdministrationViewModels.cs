using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
namespace Portal.Web.Models.Users;
public sealed class CreateManagedUserViewModel
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128, MinimumLength=12)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<string> Roles { get; set; } = new();
    [Required, StringLength(300, MinimumLength=5)] public string Reason { get; set; } = "";
    [BindNever, ValidateNever] public IReadOnlyList<string> AvailableRoles { get; set; } = new List<string>();
}
public sealed class EditManagedUserViewModel
{
    [Required, StringLength(450)] public string Id { get; set; } = "";
    [Required, StringLength(256)] public string ExpectedConcurrencyStamp { get; set; } = "";
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = new();
    [Required, StringLength(300, MinimumLength=5)] public string Reason { get; set; } = "";
    [BindNever, ValidateNever] public string Email { get; set; } = "";
    [BindNever, ValidateNever] public bool RequiresReload { get; set; }
    [BindNever, ValidateNever] public IReadOnlyList<string> AvailableRoles { get; set; } = new List<string>();
}
