using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Models.Customers;

public sealed class GrantCoverageForm
{
    public Guid CustomerId { get; set; }
    public Guid AssignmentId { get; set; }
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [Required] public DateOnly? From { get; set; }
    [Required] public DateOnly? To { get; set; }
    [Required, StringLength(250, MinimumLength = 5)]
    public string Reason { get; set; } = "";
}
