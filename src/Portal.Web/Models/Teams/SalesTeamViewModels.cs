using System;
using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Models.Teams;

public sealed class CreateSalesTeamViewModel
{
    [Required(ErrorMessage = "Ingresa el código.")]
    [StringLength(20, MinimumLength = 2)]
    [RegularExpression(
        @"[A-Za-z0-9][A-Za-z0-9_-]{1,19}",
        ErrorMessage = "Usa letras, números, guion o guion bajo.")]
    [Display(Name = "Código")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresa el nombre.")]
    [StringLength(120)]
    [Display(Name = "Nombre del equipo")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresa el correo del supervisor.")]
    [EmailAddress(ErrorMessage = "Ingresa un correo válido.")]
    [StringLength(256)]
    [Display(Name = "Correo del supervisor")]
    public string SupervisorEmail { get; set; } = string.Empty;

    public bool ConfirmSupervisorAssignment { get; set; }
}

public sealed class AddSalesTeamMemberViewModel
{
    public Guid TeamId { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string SellerEmail { get; set; } = string.Empty;

    [Required]
    [StringLength(16)]
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class RemoveSalesTeamMemberViewModel
{
    public Guid TeamId { get; set; }

    public Guid MembershipId { get; set; }

    [Required]
    [StringLength(16)]
    public string RowVersion { get; set; } = string.Empty;

    public bool ConfirmRemoval { get; set; }
}