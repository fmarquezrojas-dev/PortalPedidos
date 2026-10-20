using System;
using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Models.Customers;

public sealed class AssignCustomerPortfolioViewModel
{
    public Guid CustomerId { get; set; }

    [Required(ErrorMessage = "Indica el correo del vendedor.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(256)]
    public string SellerEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica el motivo de la asignación.")]
    [StringLength(
        250,
        MinimumLength = 5,
        ErrorMessage = "El motivo debe tener entre 5 y 250 caracteres.")]
    public string Reason { get; set; } = string.Empty;

    public Guid? ExpectedAssignmentId { get; set; }

    [StringLength(16)]
    public string? ExpectedRowVersion { get; set; }
}

public sealed class ReleaseCustomerPortfolioViewModel
{
    public Guid CustomerId { get; set; }

    public Guid ExpectedAssignmentId { get; set; }

    [Required]
    [StringLength(16)]
    public string ExpectedRowVersion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica el motivo del retiro.")]
    [StringLength(
        250,
        MinimumLength = 5,
        ErrorMessage = "El motivo debe tener entre 5 y 250 caracteres.")]
    public string Reason { get; set; } = string.Empty;

    public bool ConfirmRelease { get; set; }
}